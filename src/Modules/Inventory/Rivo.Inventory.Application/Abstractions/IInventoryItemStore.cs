using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.Abstractions;

/// <summary>
/// Persistência de `inventory`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IInventoryItemStore
{
    Task<InventoryItem?> FindAsync(Guid itemId, CancellationToken cancellationToken);

    Task<InventoryItem?> FindForUpdateAsync(Guid itemId, CancellationToken cancellationToken);

    Task<InventoryItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItem>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(InventoryItem item, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
