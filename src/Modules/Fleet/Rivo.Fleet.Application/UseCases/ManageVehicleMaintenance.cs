using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>Abre um registo de manutenção. Só um de cada vez por viatura.</summary>
public sealed class OpenMaintenance(IVehicleStore store, IAuditTrail audit)
{
    public async Task<OpenMaintenanceResult> ExecuteAsync(
        Guid vehicleId,
        MaintenanceType type,
        string description,
        DateOnly startedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return OpenMaintenanceResult.NotFound();
        }

        MaintenanceRecord registo;

        try
        {
            registo = veiculo.OpenMaintenance(type, description, startedOn);
        }
        catch (ArgumentException error)
        {
            return OpenMaintenanceResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Inactiva ou já em manutenção: os dois são conflito com o
            // estado actual da viatura, não pedido malformado — 409, não 400.
            return OpenMaintenanceResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.MaintenanceOpened,
                FleetAuditEntityTypes.Maintenance,
                registo.Id.ToString(),
                context,
                NewValue: $$"""{"vehicleId":"{{vehicleId}}","type":"{{registo.Type}}","startedOn":"{{registo.StartedOn}}"}"""),
            cancellationToken);

        return OpenMaintenanceResult.Success(registo.Id);
    }
}

public sealed record OpenMaintenanceResult(OpenMaintenanceOutcome Outcome, Guid? MaintenanceId, string? Error)
{
    public static OpenMaintenanceResult Success(Guid maintenanceId) =>
        new(OpenMaintenanceOutcome.Opened, maintenanceId, null);

    public static OpenMaintenanceResult NotFound() =>
        new(OpenMaintenanceOutcome.NotFound, null, "Viatura não encontrada.");

    public static OpenMaintenanceResult Rejected(string error) =>
        new(OpenMaintenanceOutcome.Rejected, null, error);

    public static OpenMaintenanceResult Conflict(string error) =>
        new(OpenMaintenanceOutcome.Conflict, null, error);
}

public enum OpenMaintenanceOutcome
{
    Opened,
    NotFound,

    /// <summary>Pedido malformado — descrição vazia. 400.</summary>
    Rejected,

    /// <summary>Viatura inactiva ou já em manutenção. 409.</summary>
    Conflict,
}

/// <summary>Fecha o registo de manutenção aberto e devolve a viatura a activa.</summary>
public sealed class CloseMaintenance(IVehicleStore store, IAuditTrail audit)
{
    public async Task<MaintenanceLifecycleOutcome> ExecuteAsync(
        Guid vehicleId,
        Guid maintenanceId,
        DateOnly endedOn,
        decimal? cost,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return MaintenanceLifecycleOutcome.VehicleNotFound;
        }

        if (veiculo.Maintenances.All(m => m.Id != maintenanceId))
        {
            return MaintenanceLifecycleOutcome.MaintenanceNotFound;
        }

        try
        {
            veiculo.CloseMaintenance(maintenanceId, endedOn, cost);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return MaintenanceLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.MaintenanceClosed,
                FleetAuditEntityTypes.Maintenance,
                maintenanceId.ToString(),
                context,
                NewValue: $$"""{"endedOn":"{{endedOn}}","cost":{{(cost is { } valor ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}}}"""),
            cancellationToken);

        return MaintenanceLifecycleOutcome.Closed;
    }
}

public enum MaintenanceLifecycleOutcome
{
    Closed,
    VehicleNotFound,
    MaintenanceNotFound,

    /// <summary>Já estava fechado, a data de fecho é anterior ao início, ou o custo é negativo.</summary>
    Rejected,
}
