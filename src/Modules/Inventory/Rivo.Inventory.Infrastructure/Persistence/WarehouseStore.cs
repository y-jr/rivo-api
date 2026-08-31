using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class WarehouseStore(InventoryDbContext context) : IWarehouseStore
{
    public async Task<Warehouse?> FindAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);

    public async Task<Warehouse?> FindForUpdateAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);

    public async Task<Warehouse?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Warehouse>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = context.Warehouses.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(w => w.Status == WarehouseStatus.Active);
        }

        return await query.OrderBy(w => w.Code).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken) =>
        await context.Warehouses.AddAsync(warehouse, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
