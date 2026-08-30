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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
