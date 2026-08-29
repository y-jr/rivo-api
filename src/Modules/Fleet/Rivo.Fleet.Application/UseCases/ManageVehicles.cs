using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.UseCases;

public sealed class ListVehicles(IVehicleStore store)
{
    public async Task<IReadOnlyList<Vehicle>> ExecuteAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        await store.ListAsync(includeInactive, cancellationToken);
}

public sealed class GetVehicle(IVehicleStore store)
{
    public Task<Vehicle?> ExecuteAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        store.FindAsync(vehicleId, cancellationToken);
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

public sealed class SetVehicleMaintenance(IVehicleStore store, IAuditTrail audit)
{
    public async Task<SetMaintenanceOutcome> ExecuteAsync(
        Guid vehicleId, bool inMaintenance, AuditContext context, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return SetMaintenanceOutcome.NotFound;
        }

        try
        {
            if (inMaintenance)
            {
                veiculo.SendToMaintenance();
            }
            else
            {
                veiculo.ReturnFromMaintenance();
            }
        }
        catch (InvalidOperationException)
        {
            return SetMaintenanceOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                inMaintenance ? FleetAuditActions.VehicleSentToMaintenance : FleetAuditActions.VehicleReturnedFromMaintenance,
                FleetAuditEntityTypes.Vehicle,
                veiculo.Id.ToString(),
                context),
            cancellationToken);

        return SetMaintenanceOutcome.Applied;
    }
}

public sealed record RegisterVehicleResult(bool Succeeded, Guid? VehicleId, string? Error)
{
    public static RegisterVehicleResult Success(Guid vehicleId) => new(true, vehicleId, null);

    public static RegisterVehicleResult Rejected(string error) => new(false, null, error);

    public static RegisterVehicleResult Duplicate(Guid existingId) =>
        new(false, existingId, "Matrícula já existente.");
}

public enum SetMaintenanceOutcome
{
    Applied,
    NotFound,
    Rejected,
}

public static class FleetAuditActions
{
    public const string VehicleRegistered = "fleet.vehicle.registered";
    public const string VehicleSentToMaintenance = "fleet.vehicle.sent_to_maintenance";
    public const string VehicleReturnedFromMaintenance = "fleet.vehicle.returned_from_maintenance";
    public const string VehicleDeactivated = "fleet.vehicle.deactivated";
}

public static class FleetAuditEntityTypes
{
    public const string Vehicle = "fleet.vehicle";
}
