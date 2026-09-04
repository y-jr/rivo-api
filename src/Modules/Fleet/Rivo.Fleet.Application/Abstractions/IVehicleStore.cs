using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.Abstractions;

/// <summary>
/// Persistência de `fleet`. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IVehicleStore
{
    Task<Vehicle?> FindAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<Vehicle?> FindForUpdateAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<Vehicle?> FindByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<Vehicle>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>
    /// Viaturas com pelo menos um plano de manutenção activo devido até
    /// <paramref name="asOf"/> mais <paramref name="withinDays"/> dias — a
    /// superfície de "alerta": inclui o já atrasado e o que se aproxima.
    /// </summary>
    Task<IReadOnlyList<Vehicle>> ListWithDuePlansAsync(
        DateOnly asOf, int withinDays, CancellationToken cancellationToken);

    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);

    /// <summary>
    /// Soma de <c>FleetExpense.Amount</c> (combustível, portagens,
    /// estacionamento) ocorrida no período, para toda a frota — não por
    /// viatura. Primeiro consumidor: Analytics & IA (módulo 10).
    /// </summary>
    Task<decimal> SumExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Distância percorrida no período (<c>EndOdometer − StartOdometer</c>,
    /// somada sobre as viagens), para toda a frota.
    /// </summary>
    Task<decimal> SumTripDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    // --- Documentos anexados (ADR-009: a ligação vive aqui, não em `documents`) ---

    Task AddVehicleDocumentAsync(VehicleDocument link, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleDocument>> ListVehicleDocumentsAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
