using Rivo.Audit.Contracts;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Contracts;
using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Application.UseCases;

public sealed class ListCustomers(ICustomerStore store)
{
    public async Task<IReadOnlyList<CustomerReference>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var clientes = await store.ListAsync(includeInactive, cancellationToken);

        return [.. clientes.Select(CustomerDirectory.ToReference)];
    }
}

public sealed class GetCustomer(ICustomerStore store)
{
    public async Task<CustomerReference?> ExecuteAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var cliente = await store.FindAsync(customerId, cancellationToken);

        return cliente is null ? null : CustomerDirectory.ToReference(cliente);
    }
}

/// <summary>Regista um cliente.</summary>
public sealed class RegisterCustomer(ICustomerStore store, IAuditTrail audit)
{
    public async Task<RegisterCustomerResult> ExecuteAsync(
        string name,
        string taxId,
        string addressDetail,
        string city,
        string country,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Customer cliente;

        try
        {
            cliente = Customer.Register(
                name,
                taxId,
                new Domain.BillingAddress(addressDetail, city, country));
        }
        catch (Exception error) when (error is ArgumentException or ArgumentNullException)
        {
            return RegisterCustomerResult.Rejected(error.Message);
        }

        // Unicidade do NIF: o agregado não vê o conjunto, logo a verificação é
        // desta camada. Não substitui o índice único — duas chamadas
        // simultâneas passam as duas aqui, e é a base de dados que decide.
        if (await store.FindByTaxIdAsync(cliente.TaxId, cancellationToken) is { } existente)
        {
            return RegisterCustomerResult.Duplicate(existente.Id);
        }

        cliente.ChangeContacts(email, phone);

        await store.AddAsync(cliente, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerRegistered,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{cliente.Name}}","taxId":"{{cliente.TaxId}}"}"""),
            cancellationToken);

        return RegisterCustomerResult.Success(cliente.Id);
    }
}

public sealed record RegisterCustomerResult(
    RegisterCustomerOutcome Outcome,
    Guid? CustomerId,
    string? Error)
{
    public static RegisterCustomerResult Success(Guid customerId) =>
        new(RegisterCustomerOutcome.Registered, customerId, null);

    public static RegisterCustomerResult Rejected(string error) =>
        new(RegisterCustomerOutcome.Rejected, null, error);

    /// <param name="existingId">
    /// Devolvido de propósito: quem tentou registar quase de certeza quer
    /// trabalhar com o cliente que já existe, e sem o identificador teria de o
    /// procurar às cegas.
    /// </param>
    public static RegisterCustomerResult Duplicate(Guid existingId) =>
        new(RegisterCustomerOutcome.DuplicateTaxId, existingId, null);
}

public enum RegisterCustomerOutcome
{
    Registered,
    Rejected,
    DuplicateTaxId,
}

/// <summary>Altera os dados de um cliente.</summary>
public sealed class UpdateCustomer(ICustomerStore store, IAuditTrail audit)
{
    public async Task<UpdateCustomerOutcome> ExecuteAsync(
        Guid customerId,
        string? name,
        string? addressDetail,
        string? city,
        string? country,
        string? email,
        string? phone,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return UpdateCustomerOutcome.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            cliente.Rename(name);
        }

        // A morada substitui-se inteira ou não se toca: é objecto de valor, e
        // aceitar só a cidade deixaria uma morada meio antiga e meio nova.
        if (addressDetail is not null || city is not null || country is not null)
        {
            if (string.IsNullOrWhiteSpace(addressDetail)
                || string.IsNullOrWhiteSpace(city)
                || string.IsNullOrWhiteSpace(country))
            {
                return UpdateCustomerOutcome.PartialAddress;
            }

            cliente.ChangeBillingAddress(new Domain.BillingAddress(addressDetail, city, country));
        }

        cliente.ChangeContacts(email, phone);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                CommercialAuditActions.CustomerUpdated,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{cliente.Name}}"}"""),
            cancellationToken);

        return UpdateCustomerOutcome.Updated;
    }
}

public enum UpdateCustomerOutcome
{
    Updated,
    NotFound,

    /// <summary>Morada indicada em parte. Ou vai inteira, ou não vai.</summary>
    PartialAddress,
}

/// <summary>
/// Desactiva ou reactiva um cliente.
///
/// <para>
/// Não há eliminação, e é BR-14: um cliente referenciado por facturas emitidas
/// é parte desses documentos.
/// </para>
/// </summary>
public sealed class SetCustomerStatus(ICustomerStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid customerId,
        bool active,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var cliente = await store.FindForUpdateAsync(customerId, cancellationToken);

        if (cliente is null)
        {
            return false;
        }

        if (active)
        {
            cliente.Reactivate();
        }
        else
        {
            cliente.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active ? CommercialAuditActions.CustomerReactivated : CommercialAuditActions.CustomerDeactivated,
                CommercialAuditEntityTypes.Customer,
                cliente.Id.ToString(),
                context,
                NewValue: $$"""{"status":"{{cliente.Status}}"}"""),
            cancellationToken);

        return true;
    }
}

/// <summary>Acções de `commercial` na trilha de auditoria.</summary>
public static class CommercialAuditActions
{
    public const string CustomerRegistered = "commercial.customer.registered";
    public const string CustomerUpdated = "commercial.customer.updated";
    public const string CustomerDeactivated = "commercial.customer.deactivated";
    public const string CustomerReactivated = "commercial.customer.reactivated";
}

public static class CommercialAuditEntityTypes
{
    public const string Customer = "commercial.customer";
}
