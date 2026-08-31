using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class InventoryCountStore(InventoryDbContext context) : IInventoryCountStore
{
    public async Task<InventoryCount?> FindAsync(Guid countId, CancellationToken cancellationToken) =>
        await context.InventoryCounts.AsNoTracking()
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == countId, cancellationToken);

    public async Task<InventoryCount?> FindForUpdateAsync(Guid countId, CancellationToken cancellationToken) =>
        await context.InventoryCounts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == countId, cancellationToken);

    public async Task<IReadOnlyList<InventoryCount>> ListAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var query = context.InventoryCounts.AsNoTracking()
            .Include(c => c.Lines)
            .AsQueryable();

        if (warehouseId is { } id)
        {
            query = query.Where(c => c.WarehouseId == id);
        }

        return await query.OrderByDescending(c => c.OccurredOn).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(InventoryCount count, CancellationToken cancellationToken) =>
        await context.InventoryCounts.AddAsync(count, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
