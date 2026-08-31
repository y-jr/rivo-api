using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.Abstractions;

/// <summary>
/// Persistência de <see cref="Warehouse"/>. Definida aqui e implementada em
/// Infrastructure, mesma disciplina de <see cref="IInventoryItemStore"/>.
/// </summary>
public interface IWarehouseStore
{
    Task<Warehouse?> FindAsync(Guid warehouseId, CancellationToken cancellationToken);

    Task<Warehouse?> FindForUpdateAsync(Guid warehouseId, CancellationToken cancellationToken);

    Task<Warehouse?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Warehouse>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
