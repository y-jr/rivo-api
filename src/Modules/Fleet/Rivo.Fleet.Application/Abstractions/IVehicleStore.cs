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

    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
