using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;
using Rivo.Hr.Contracts;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>
/// Regista uma viagem já concluída.
///
/// <para>
/// <strong>O motorista, quando indicado, tem de existir em `hr`</strong>
/// (ADR-010) — lido pelo contrato, nunca copiado (BR-18). Mesma verificação
/// de <c>AssignVehicle</c>, mas opcional aqui: uma viagem pode não ter
/// motorista atribuído formalmente.
/// </para>
/// </summary>
public sealed class RegisterTrip(IVehicleStore store, IEmployeeDirectory employees, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterTripResult> ExecuteAsync(
        Guid vehicleId,
        Guid? driverId,
        DateOnly startedOn,
        DateOnly endedOn,
        decimal startOdometer,
        decimal endOdometer,
        string? purpose,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return RegisterTripResult.VehicleNotFound();
        }

        if (driverId is { } id)
        {
            var motorista = await employees.FindAsync(id, clock.GetUtcNow(), cancellationToken);

            if (motorista is null)
            {
                return RegisterTripResult.DriverNotFound();
            }
        }

        VehicleTrip viagem;

        try
        {
            viagem = veiculo.RegisterTrip(driverId, startedOn, endedOn, startOdometer, endOdometer, purpose);
        }
        catch (ArgumentException error)
        {
            return RegisterTripResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return RegisterTripResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.TripRegistered,
                FleetAuditEntityTypes.Trip,
                viagem.Id.ToString(),
                context,
                NewValue: $$"""{"vehicleId":"{{vehicleId}}","distance":{{viagem.Distance}}}"""),
            cancellationToken);

        return RegisterTripResult.Success(viagem.Id, viagem.Distance);
    }
}

public sealed record RegisterTripResult(RegisterTripOutcome Outcome, Guid? TripId, decimal? Distance, string? Error)
{
    public static RegisterTripResult Success(Guid tripId, decimal distance) =>
        new(RegisterTripOutcome.Registered, tripId, distance, null);

    public static RegisterTripResult VehicleNotFound() =>
        new(RegisterTripOutcome.VehicleNotFound, null, null, "Viatura não encontrada.");

    public static RegisterTripResult DriverNotFound() =>
        new(RegisterTripOutcome.DriverNotFound, null, null, "Motorista não encontrado.");

    public static RegisterTripResult Rejected(string error) =>
        new(RegisterTripOutcome.Rejected, null, null, error);

    public static RegisterTripResult Conflict(string error) =>
        new(RegisterTripOutcome.Conflict, null, null, error);
}

public enum RegisterTripOutcome
{
    Registered,
    VehicleNotFound,
    DriverNotFound,

    /// <summary>Pedido malformado — datas ou odómetros inconsistentes. 400.</summary>
    Rejected,

    /// <summary>Viatura inactiva. 409.</summary>
    Conflict,
}
