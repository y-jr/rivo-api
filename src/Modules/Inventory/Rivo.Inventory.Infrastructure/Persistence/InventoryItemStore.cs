using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class InventoryItemStore(InventoryDbContext context) : IInventoryItemStore
{
    public async Task<InventoryItem?> FindAsync(Guid itemId, CancellationToken cancellationToken) =>
        await context.Items.AsNoTracking()
            .Include(i => i.Movements)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    public async Task<InventoryItem?> FindForUpdateAsync(Guid itemId, CancellationToken cancellationToken) =>
        await context.Items
            .Include(i => i.Movements)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

    public async Task<InventoryItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken) =>
        await context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Sku == sku, cancellationToken);

    public async Task<(IReadOnlyList<InventoryItem> Items, int? TotalCount)> ListAsync(
        bool includeInactive, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Items.AsNoTracking()
            .Include(i => i.Movements)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(i => i.Status == InventoryItemStatus.Active);
        }

        query = query.OrderBy(i => i.Sku);

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        return (await query.ToListAsync(cancellationToken), total);
    }

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken) =>
        await context.Items.AddAsync(item, cancellationToken);

    public async Task<decimal> SumCurrentStockValueAsync(CancellationToken cancellationToken) =>
        await context.Items
            .AsNoTracking()
            .Where(i => i.Status == InventoryItemStatus.Active)
            .SumAsync(i => (decimal?)(i.QuantityOnHand * i.AverageCost), cancellationToken) ?? 0m;

    public async Task<decimal> SumMovementValueInPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await context.Set<StockMovement>()
            .AsNoTracking()
            .Where(m => m.OccurredOn >= from && m.OccurredOn <= to)
            .SumAsync(m => (decimal?)(m.Quantity * m.UnitCost), cancellationToken) ?? 0m;

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
