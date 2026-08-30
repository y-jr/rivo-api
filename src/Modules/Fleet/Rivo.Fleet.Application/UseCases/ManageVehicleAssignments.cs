using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;
using Rivo.Hr.Contracts;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>
/// Atribui uma viatura a um motorista.
///
/// <para>
/// <strong>O Colaborador tem de existir em `hr`</strong> (ADR-010) — lido
/// pelo contrato, nunca copiado (BR-18). Mesma verificação que a atribuição
/// de Tarefa faz em `projects`.
/// </para>
/// </summary>
public sealed class AssignVehicle(IVehicleStore store, IEmployeeDirectory employees, IAuditTrail audit, TimeProvider clock)
{
    public async Task<AssignVehicleResult> ExecuteAsync(
        Guid vehicleId,
        Guid employeeId,
        DateOnly startedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return AssignVehicleResult.VehicleNotFound();
        }

        var colaborador = await employees.FindAsync(employeeId, clock.GetUtcNow(), cancellationToken);

        if (colaborador is null)
        {
            return AssignVehicleResult.EmployeeNotFound();
        }

        VehicleAssignment atribuicao;

        try
        {
            atribuicao = veiculo.Assign(employeeId, startedOn);
        }
        catch (ArgumentException error)
        {
            return AssignVehicleResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Inactiva ou já atribuída: conflito com o estado actual da
            // viatura, não pedido malformado — 409, não 400.
            return AssignVehicleResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.AssignmentOpened,
                FleetAuditEntityTypes.Assignment,
                atribuicao.Id.ToString(),
                context,
                NewValue: $$"""{"vehicleId":"{{vehicleId}}","employeeId":"{{employeeId}}","startedOn":"{{startedOn}}"}"""),
            cancellationToken);

        return AssignVehicleResult.Success(atribuicao.Id);
    }
}

public sealed record AssignVehicleResult(AssignVehicleOutcome Outcome, Guid? AssignmentId, string? Error)
{
    public static AssignVehicleResult Success(Guid assignmentId) =>
        new(AssignVehicleOutcome.Assigned, assignmentId, null);

    public static AssignVehicleResult VehicleNotFound() =>
        new(AssignVehicleOutcome.VehicleNotFound, null, "Viatura não encontrada.");

    public static AssignVehicleResult EmployeeNotFound() =>
        new(AssignVehicleOutcome.EmployeeNotFound, null, "Colaborador a atribuir não encontrado.");

    public static AssignVehicleResult Rejected(string error) =>
        new(AssignVehicleOutcome.Rejected, null, error);

    public static AssignVehicleResult Conflict(string error) =>
        new(AssignVehicleOutcome.Conflict, null, error);
}

public enum AssignVehicleOutcome
{
    Assigned,
    VehicleNotFound,
    EmployeeNotFound,

    /// <summary>Pedido malformado. 400.</summary>
    Rejected,

    /// <summary>Viatura inactiva ou já atribuída. 409.</summary>
    Conflict,
}

/// <summary>Termina a atribuição aberta de uma viatura.</summary>
public sealed class EndVehicleAssignment(IVehicleStore store, IAuditTrail audit)
{
    public async Task<AssignmentLifecycleOutcome> ExecuteAsync(
        Guid vehicleId,
        Guid assignmentId,
        DateOnly endedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return AssignmentLifecycleOutcome.VehicleNotFound;
        }

        if (veiculo.Assignments.All(a => a.Id != assignmentId))
        {
            return AssignmentLifecycleOutcome.AssignmentNotFound;
        }

        try
        {
            veiculo.EndAssignment(assignmentId, endedOn);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return AssignmentLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.AssignmentEnded,
                FleetAuditEntityTypes.Assignment,
                assignmentId.ToString(),
                context,
                NewValue: $$"""{"endedOn":"{{endedOn}}"}"""),
            cancellationToken);

        return AssignmentLifecycleOutcome.Ended;
    }
}

public enum AssignmentLifecycleOutcome
{
    Ended,
    VehicleNotFound,
    AssignmentNotFound,

    /// <summary>Já tinha terminado, ou a data de fim é anterior ao início.</summary>
    Rejected,
}
