using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Lista contratos, de toda a empresa ou de um colaborador.
/// </summary>
public sealed class ListEmploymentContracts(IHrStore store)
{
    public async Task<IReadOnlyList<EmploymentContractView>> ExecuteAsync(
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var contracts = employeeId is null
            ? await store.ListContractsAsync(cancellationToken)
            : await store.ListContractsForEmployeeAsync(employeeId.Value, cancellationToken);

        return [.. contracts.Select(Project)];
    }

    internal static EmploymentContractView Project(EmploymentContract c) =>
        new(c.Id,
            c.EmployeeId,
            c.Type.ToString(),
            c.StartsOn,
            c.EndsOn,
            c.MonthlySalary,
            c.Currency,
            c.Status.ToString(),
            c.Notes);
}

/// <summary>
/// Vista de um contrato na fronteira HTTP.
///
/// <para>
/// <strong>Leva <c>EmployeeId</c> e não o nome</strong> — ADR-010. Quem
/// apresenta a lista resolve os nomes uma vez por <c>GET /hr/employees</c>, em
/// vez de o contrato guardar uma cópia que envelhece.
/// </para>
/// </summary>
public sealed record EmploymentContractView(
    Guid ContractId,
    Guid EmployeeId,
    string Type,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    decimal MonthlySalary,
    string Currency,
    string Status,
    string? Notes);

/// <summary>
/// Celebra um contrato de trabalho.
///
/// <para>
/// A regra que não cabe na entidade vive aqui: <strong>não pode haver duas
/// relações laborais em vigor ao mesmo tempo com a mesma pessoa.</strong> O
/// critério de sobreposição é do domínio
/// (<see cref="EmploymentContract.OverlapsWith"/>); ver os outros contratos é
/// que exige o repositório, e por isso a verificação corre neste nível.
/// </para>
/// </summary>
public sealed class DrawEmploymentContract(IHrStore store, IAuditTrail audit)
{
    /// <summary>
    /// Tipos aceites, para a mensagem de recusa e para quem construa a
    /// interface. Derivados do enum do domínio, e não escritos à mão: uma lista
    /// paralela ficaria desactualizada no dia em que nascesse um tipo novo.
    /// </summary>
    public static readonly IReadOnlyList<string> ContractTypes =
        [.. Enum.GetNames<EmploymentContractType>()];


    /// <param name="type">
    /// Nome do tipo de contrato. Recebido como texto e convertido aqui, e não
    /// na camada API: <c>EmploymentContractType</c> é do domínio, e a fronteira
    /// HTTP não o conhece (architecture/dependency-rules.md). Um tipo
    /// desconhecido é recusado como termo inválido, que é o que é.
    /// </param>
    public async Task<DrawContractResult> ExecuteAsync(
        Guid employeeId,
        string type,
        DateOnly startsOn,
        DateOnly? endsOn,
        decimal monthlySalary,
        string currency,
        string? notes,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EmploymentContractType>(type, ignoreCase: true, out var contractType))
        {
            return DrawContractResult.InvalidTerms(
                $"Tipo de contrato desconhecido. Esperado: {string.Join(", ", ContractTypes)}.");
        }

        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return DrawContractResult.EmployeeNotFound();
        }

        var existing = await store.ListContractsForEmployeeAsync(employeeId, cancellationToken);

        if (existing.Any(c => c.OverlapsWith(startsOn, endsOn)))
        {
            return DrawContractResult.OverlapsExistingContract();
        }

        EmploymentContract contract;

        try
        {
            contract = EmploymentContract.Draw(
                employeeId, contractType, startsOn, endsOn, monthlySalary, currency, notes);
        }
        catch (ArgumentException error)
        {
            // Termos incoerentes são violação de regra, não falha técnica:
            // devolvem-se a quem chamou para que possa corrigir
            // (standards/error-handling.md).
            return DrawContractResult.InvalidTerms(error.Message);
        }

        await store.AddContractAsync(contract, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.ContractDrawn,
                HrAuditEntityTypes.EmploymentContract,
                contract.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{employeeId}}","type":"{{type}}"}"""),
            cancellationToken);

        return DrawContractResult.Success(contract.Id);
    }
}

public sealed record DrawContractResult(DrawContractOutcome Outcome, Guid? ContractId, string? Error)
{
    public static DrawContractResult Success(Guid id) => new(DrawContractOutcome.Drawn, id, null);

    public static DrawContractResult EmployeeNotFound() =>
        new(DrawContractOutcome.EmployeeNotFound, null, "Colaborador não encontrado.");

    public static DrawContractResult OverlapsExistingContract() =>
        new(DrawContractOutcome.Overlaps, null,
            "O colaborador já tem um contrato em vigor no período indicado.");

    public static DrawContractResult InvalidTerms(string reason) =>
        new(DrawContractOutcome.InvalidTerms, null, reason);
}

public enum DrawContractOutcome
{
    Drawn,
    EmployeeNotFound,
    Overlaps,
    InvalidTerms,
}

/// <summary>
/// Cessa um contrato — por chegada ao termo ou por rescisão.
/// </summary>
public sealed class TerminateEmploymentContract(IHrStore store, IAuditTrail audit)
{
    public async Task<TerminateContractResult> ExecuteAsync(
        Guid contractId,
        DateOnly on,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var contract = await store.FindContractAsync(contractId, cancellationToken);

        if (contract is null)
        {
            return TerminateContractResult.NotFound();
        }

        try
        {
            contract.Terminate(on);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return TerminateContractResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.ContractTerminated,
                HrAuditEntityTypes.EmploymentContract,
                contract.Id.ToString(),
                context,
                NewValue: $$"""{"endsOn":"{{on:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return TerminateContractResult.Success();
    }
}

public sealed record TerminateContractResult(TerminateContractOutcome Outcome, string? Error)
{
    public static TerminateContractResult Success() => new(TerminateContractOutcome.Terminated, null);

    public static TerminateContractResult NotFound() =>
        new(TerminateContractOutcome.NotFound, "Contrato não encontrado.");

    public static TerminateContractResult Rejected(string reason) =>
        new(TerminateContractOutcome.Rejected, reason);
}

public enum TerminateContractOutcome
{
    Terminated,
    NotFound,
    Rejected,
}
