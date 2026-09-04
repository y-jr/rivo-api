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

/// <summary>
/// Leitura agregada de actividade de frota para composição (Analytics &amp;
/// IA, módulo 10) — despesa, distância percorrida e custo de manutenção por
/// período, para toda a frota, não por viatura.
/// </summary>
public interface IFleetActivityOverview
{
    /// <summary>Soma de despesas (combustível, portagens, estacionamento) ocorridas no período, toda a frota.</summary>
    Task<decimal> GetPeriodExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Distância percorrida no período, toda a frota.</summary>
    Task<decimal> GetPeriodDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Soma do custo das manutenções fechadas no período (por
    /// <c>EndedOn</c>, não <c>StartedOn</c> — é quando o custo passa a
    /// existir), toda a frota. <c>MaintenanceRecord.Cost</c> é opcional
    /// (ADR-048): manutenções sem custo registado não entram na soma, não
    /// contam como zero.
    /// </summary>
    Task<decimal> GetPeriodMaintenanceCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
