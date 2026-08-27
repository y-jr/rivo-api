using Rivo.Audit.Contracts;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Contracts;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.UseCases;

public sealed class ListSuppliers(IProcurementStore store)
{
    public async Task<IReadOnlyList<SupplierReference>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var fornecedores = await store.ListSuppliersAsync(includeInactive, cancellationToken);

        return [.. fornecedores.Select(SupplierDirectory.ToReference)];
    }
}

public sealed class GetSupplier(IProcurementStore store)
{
    public async Task<SupplierReference?> ExecuteAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var fornecedor = await store.FindSupplierAsync(supplierId, cancellationToken);

        return fornecedor is null ? null : SupplierDirectory.ToReference(fornecedor);
    }
}

/// <summary>Qualifica um fornecedor.</summary>
public sealed class RegisterSupplier(IProcurementStore store, IAuditTrail audit)
{
    public async Task<RegisterSupplierResult> ExecuteAsync(
        string name,
        string taxId,
        string? iban,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Supplier fornecedor;

        try
        {
            fornecedor = Supplier.Register(name, taxId);
            fornecedor.SetIban(iban);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentNullException)
        {
            return RegisterSupplierResult.Rejected(error.Message);
        }

        // Unicidade do NIF: o agregado não vê o conjunto, logo a verificação é
        // desta camada. Não substitui o índice único — duas chamadas
        // simultâneas passam as duas aqui, e é a base de dados que decide.
        if (await store.FindSupplierByTaxIdAsync(fornecedor.TaxId, cancellationToken) is { } existente)
        {
            return RegisterSupplierResult.Duplicate(existente.Id);
        }

        fornecedor.ChangeContacts(email, phone);

        await store.AddSupplierAsync(fornecedor, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        // O IBAN entra na trilha porque é o campo que decide para onde o
        // dinheiro sai. Alterá-lo é o passo silencioso de uma fraude de
        // pagamento, e sem rasto não se distingue de uma correcção legítima.
        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.SupplierRegistered,
                ProcurementAuditEntityTypes.Supplier,
                fornecedor.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{fornecedor.Name}}","taxId":"{{fornecedor.TaxId}}","iban":"{{fornecedor.Iban}}"}"""),
            cancellationToken);

        return RegisterSupplierResult.Success(fornecedor.Id);
    }
}

public sealed record RegisterSupplierResult(
    RegisterSupplierOutcome Outcome,
    Guid? SupplierId,
    string? Error)
{
    public static RegisterSupplierResult Success(Guid supplierId) =>
        new(RegisterSupplierOutcome.Registered, supplierId, null);

    public static RegisterSupplierResult Rejected(string error) =>
        new(RegisterSupplierOutcome.Rejected, null, error);

    /// <param name="existingId">
    /// Devolvido de propósito: quem tentou registar quase de certeza quer
    /// trabalhar com o fornecedor que já existe.
    /// </param>
    public static RegisterSupplierResult Duplicate(Guid existingId) =>
        new(RegisterSupplierOutcome.DuplicateTaxId, existingId, null);
}

public enum RegisterSupplierOutcome
{
    Registered,
    Rejected,
    DuplicateTaxId,
}

/// <summary>Altera os dados de um fornecedor.</summary>
public sealed class UpdateSupplier(IProcurementStore store, IAuditTrail audit)
{
    public async Task<UpdateSupplierResult> ExecuteAsync(
        Guid supplierId,
        string? name,
        string? iban,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var fornecedor = await store.FindSupplierForUpdateAsync(supplierId, cancellationToken);

        if (fornecedor is null)
        {
            return UpdateSupplierResult.NotFound();
        }

        var ibanAnterior = fornecedor.Iban;

        try
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                fornecedor.Rename(name);
            }

            // `iban` nulo não apaga: apagar é acto próprio, e um corpo parcial
            // que omite o campo não pode significar "tira-lhe a conta".
            if (iban is not null)
            {
                fornecedor.SetIban(iban);
            }
        }
        catch (ArgumentException error)
        {
            return UpdateSupplierResult.Rejected(error.Message);
        }

        fornecedor.ChangeContacts(email, phone);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.SupplierUpdated,
                ProcurementAuditEntityTypes.Supplier,
                fornecedor.Id.ToString(),
                context,
                PreviousValue: $$"""{"iban":"{{ibanAnterior}}"}""",
                NewValue: $$"""{"name":"{{fornecedor.Name}}","iban":"{{fornecedor.Iban}}"}"""),
            cancellationToken);

        return UpdateSupplierResult.Updated();
    }
}

public sealed record UpdateSupplierResult(UpdateSupplierOutcome Outcome, string? Error)
{
    public static UpdateSupplierResult Updated() => new(UpdateSupplierOutcome.Updated, null);

    public static UpdateSupplierResult NotFound() => new(UpdateSupplierOutcome.NotFound, null);

    public static UpdateSupplierResult Rejected(string error) =>
        new(UpdateSupplierOutcome.Rejected, error);
}

public enum UpdateSupplierOutcome
{
    Updated,
    NotFound,
    Rejected,
}

/// <summary>
/// Desactiva ou reactiva um fornecedor. Não há eliminação — BR-14.
/// </summary>
public sealed class SetSupplierStatus(IProcurementStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid supplierId,
        bool active,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var fornecedor = await store.FindSupplierForUpdateAsync(supplierId, cancellationToken);

        if (fornecedor is null)
        {
            return false;
        }

        if (active)
        {
            fornecedor.Reactivate();
        }
        else
        {
            fornecedor.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active
                    ? ProcurementAuditActions.SupplierReactivated
                    : ProcurementAuditActions.SupplierDeactivated,
                ProcurementAuditEntityTypes.Supplier,
                fornecedor.Id.ToString(),
                context,
                NewValue: $$"""{"status":"{{fornecedor.Status}}"}"""),
            cancellationToken);

        return true;
    }
}

/// <summary>Acções de `procurement` na trilha de auditoria.</summary>
public static class ProcurementAuditActions
{
    public const string SupplierRegistered = "procurement.supplier.registered";
    public const string SupplierUpdated = "procurement.supplier.updated";
    public const string SupplierDeactivated = "procurement.supplier.deactivated";
    public const string SupplierReactivated = "procurement.supplier.reactivated";

    public const string RequisitionOpened = "procurement.requisition.opened";
    public const string RequisitionSubmitted = "procurement.requisition.submitted";
    public const string RequisitionApproved = "procurement.requisition.approved";
    public const string RequisitionRefused = "procurement.requisition.refused";
    public const string RequisitionCancelled = "procurement.requisition.cancelled";
}

public static class ProcurementAuditEntityTypes
{
    public const string Supplier = "procurement.supplier";
    public const string Requisition = "procurement.purchase_requisition";
}
