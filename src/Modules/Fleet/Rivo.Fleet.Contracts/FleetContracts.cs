namespace Rivo.Fleet.Contracts;

/// <summary>
/// Superfície publicada de `fleet`. Assembly sem dependências (ADR-017).
/// </summary>
public static class FleetPermissions
{
    public const string VehiclesRead = "fleet.vehicles.read";
    public const string VehiclesWrite = "fleet.vehicles.write";

    public static readonly IReadOnlyList<string> All = [VehiclesRead, VehiclesWrite];
}

/// <summary>
/// Referência de leitura a uma Viatura, para quem precisa de confirmar que
/// existe sem lhe possuir o registo — primeiro consumidor: `projects`,
/// Alocação de Recursos (2026-08-31).
///
/// <para>
/// Mesmo desenho de <c>IEmployeeDirectory</c> (`hr`, ADR-010): os
/// consumidores guardam apenas <see cref="VehicleId"/> e lêem os atributos
/// por aqui — nunca copiam matrícula nem modelo para as suas tabelas.
/// </para>
/// </summary>
public interface IVehicleDirectory
{
    Task<VehicleReference?> FindAsync(Guid vehicleId, CancellationToken cancellationToken);
}

public sealed record VehicleReference(Guid VehicleId, string PlateNumber, string Model, string Status);
