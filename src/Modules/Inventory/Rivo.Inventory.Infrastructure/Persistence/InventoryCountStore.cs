using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;
using Rivo.SharedKernel.Contracts;

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

    public async Task<(IReadOnlyList<InventoryCount> Items, int? TotalCount)> ListAsync(
        Guid? warehouseId, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.InventoryCounts.AsNoTracking()
            .Include(c => c.Lines)
            .AsQueryable();

        if (warehouseId is { } id)
        {
            query = query.Where(c => c.WarehouseId == id);
        }

        // Desempate por Id: sem isto, duas contagens abertas no mesmo dia não
        // têm ordem determinística, e o Skip/Take deixaria de ser fiável.
        query = query.OrderByDescending(c => c.OccurredOn).ThenByDescending(c => c.Id);

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        return (await query.ToListAsync(cancellationToken), total);
    }

    public async Task AddAsync(InventoryCount count, CancellationToken cancellationToken) =>
        await context.InventoryCounts.AddAsync(count, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
