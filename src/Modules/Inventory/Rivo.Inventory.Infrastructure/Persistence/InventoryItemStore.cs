using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class InventoryItemStore(InventoryDbContext context) : IInventoryItemStore
{
    public async Task<InventoryItem?> FindAsync(Guid itemId, CancellationToken cancellationToken) =>
        await context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    public async Task<InventoryItem?> FindForUpdateAsync(Guid itemId, CancellationToken cancellationToken) =>
        await context.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    public async Task<InventoryItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken) =>
        await context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Sku == sku, cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = context.Items.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(i => i.Status == InventoryItemStatus.Active);
        }

        return await query.OrderBy(i => i.Sku).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken) =>
        await context.Items.AddAsync(item, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
