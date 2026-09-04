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
/// IA, módulo 10) — despesa e distância percorrida por período, para toda a
/// frota, não por viatura.
///
/// <para>
/// <strong>Sem custo de manutenção.</strong> <c>MaintenanceRecord</c> não
/// tem nenhum campo de valor — o custo de uma manutenção nunca foi
/// capturado no domínio, e inventar aqui um número que a manutenção não
/// guarda seria pior do que não ter a métrica (mesmo princípio do
/// ADR-036 para códigos de isenção). Fica registado em
/// `pending-decisions.md`.
/// </para>
/// </summary>
public interface IFleetActivityOverview
{
    /// <summary>Soma de despesas (combustível, portagens, estacionamento) ocorridas no período, toda a frota.</summary>
    Task<decimal> GetPeriodExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Distância percorrida no período, toda a frota.</summary>
    Task<decimal> GetPeriodDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
