using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>
/// Vista de leitura de uma viatura, com as suas manutenções e atribuições.
///
/// <para>
/// A entidade de domínio nunca sai desta camada (architecture/dependency-rules.md
/// §API) — mesma forma de <c>ProjectView</c> em `projects`.
/// </para>
/// </summary>
public sealed record VehicleView(
    Guid VehicleId,
    string PlateNumber,
    string Model,
    string Status,
    IReadOnlyList<MaintenanceRecordView> Maintenances,
    IReadOnlyList<VehicleAssignmentView> Assignments,
    IReadOnlyList<MaintenancePlanView> Plans);

public sealed record MaintenanceRecordView(
    Guid MaintenanceId, string Type, string Description, DateOnly StartedOn, DateOnly? EndedOn);

public sealed record VehicleAssignmentView(
    Guid AssignmentId, Guid EmployeeId, DateOnly StartedOn, DateOnly? EndedOn);

public sealed record MaintenancePlanView(
    Guid PlanId, string Description, int IntervalDays, DateOnly NextDueOn, bool IsActive, bool IsOverdue);

internal static class VehicleViews
{
    internal static VehicleView ToView(Vehicle veiculo, DateOnly asOf) => new(
        veiculo.Id,
        veiculo.PlateNumber,
        veiculo.Model,
        veiculo.Status.ToString(),
        [.. veiculo.Maintenances.Select(m =>
            new MaintenanceRecordView(m.Id, m.Type.ToString(), m.Description, m.StartedOn, m.EndedOn))],
        [.. veiculo.Assignments.Select(a =>
            new VehicleAssignmentView(a.Id, a.EmployeeId, a.StartedOn, a.EndedOn))],
        [.. veiculo.Plans.Select(p =>
            new MaintenancePlanView(p.Id, p.Description, p.IntervalDays, p.NextDueOn, p.IsActive, p.IsOverdue(asOf)))]);
}

public sealed class ListVehicles(IVehicleStore store, TimeProvider clock)
{
    public async Task<IReadOnlyList<VehicleView>> ExecuteAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var veiculos = await store.ListAsync(includeInactive, cancellationToken);
        return [.. veiculos.Select(v => VehicleViews.ToView(v, hoje))];
    }
}

public sealed class GetVehicle(IVehicleStore store, TimeProvider clock)
{
    public async Task<VehicleView?> ExecuteAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindAsync(vehicleId, cancellationToken);
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return veiculo is null ? null : VehicleViews.ToView(veiculo, hoje);
    }
}

/// <summary>
/// Desactiva uma viatura. Nunca elimina — o histórico da viatura fica.
/// </summary>
public sealed class DeactivateVehicle(IVehicleStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(Guid vehicleId, AuditContext context, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return false;
        }

        veiculo.Deactivate();

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.VehicleDeactivated,
                FleetAuditEntityTypes.Vehicle,
                veiculo.Id.ToString(),
                context),
            cancellationToken);

        return true;
    }
}

public sealed class RegisterVehicle(IVehicleStore store, IAuditTrail audit)
{
    public async Task<RegisterVehicleResult> ExecuteAsync(
        string plateNumber,
        string model,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Vehicle veiculo;

        try
        {
            veiculo = Vehicle.Register(plateNumber, model);
        }
        catch (ArgumentException error)
        {
            return RegisterVehicleResult.Rejected(error.Message);
        }

        if (await store.FindByPlateNumberAsync(veiculo.PlateNumber, cancellationToken) is { } existente)
        {
            return RegisterVehicleResult.Duplicate(existente.Id);
        }

        await store.AddAsync(veiculo, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.VehicleRegistered,
                FleetAuditEntityTypes.Vehicle,
                veiculo.Id.ToString(),
                context,
                NewValue: $$"""{"plateNumber":"{{veiculo.PlateNumber}}","model":"{{veiculo.Model}}"}"""),
            cancellationToken);

        return RegisterVehicleResult.Success(veiculo.Id);
    }
}

public sealed record RegisterVehicleResult(bool Succeeded, Guid? VehicleId, string? Error)
{
    public static RegisterVehicleResult Success(Guid vehicleId) => new(true, vehicleId, null);

    public static RegisterVehicleResult Rejected(string error) => new(false, null, error);

    public static RegisterVehicleResult Duplicate(Guid existingId) =>
        new(false, existingId, "Matrícula já existente.");
}

public static class FleetAuditActions
{
    public const string VehicleRegistered = "fleet.vehicle.registered";
    public const string VehicleDeactivated = "fleet.vehicle.deactivated";
    public const string MaintenanceOpened = "fleet.maintenance.opened";
    public const string MaintenanceClosed = "fleet.maintenance.closed";
    public const string AssignmentOpened = "fleet.assignment.opened";
    public const string AssignmentEnded = "fleet.assignment.ended";
    public const string PlanScheduled = "fleet.maintenance_plan.scheduled";
    public const string PlanCycleCompleted = "fleet.maintenance_plan.cycle_completed";
    public const string PlanCancelled = "fleet.maintenance_plan.cancelled";
}

public static class FleetAuditEntityTypes
{
    public const string Vehicle = "fleet.vehicle";
    public const string Maintenance = "fleet.maintenance";
    public const string Assignment = "fleet.assignment";
    public const string MaintenancePlan = "fleet.maintenance_plan";
}
