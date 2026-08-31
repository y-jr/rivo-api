using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.Abstractions;

/// <summary>
/// Persistência de <see cref="InventoryCount"/>. Definida aqui e
/// implementada em Infrastructure, mesma disciplina de <see cref="IInventoryItemStore"/>.
/// </summary>
public interface IInventoryCountStore
{
    Task<InventoryCount?> FindAsync(Guid countId, CancellationToken cancellationToken);

    Task<InventoryCount?> FindForUpdateAsync(Guid countId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryCount>> ListAsync(Guid? warehouseId, CancellationToken cancellationToken);

    Task AddAsync(InventoryCount count, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
