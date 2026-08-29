namespace Rivo.Fleet.Contracts;

/// <summary>
/// Superfície publicada de `fleet`. Assembly sem dependências (ADR-017).
///
/// <para>
/// Só o catálogo de permissões, por agora — sem consumidor ainda para um
/// contrato de leitura. Ver a nota equivalente em `Rivo.Projects.Contracts`.
/// </para>
/// </summary>
public static class FleetPermissions
{
    public const string VehiclesRead = "fleet.vehicles.read";
    public const string VehiclesWrite = "fleet.vehicles.write";

    public static readonly IReadOnlyList<string> All = [VehiclesRead, VehiclesWrite];
}
