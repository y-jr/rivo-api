using Microsoft.EntityFrameworkCore;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class WarehouseStore(InventoryDbContext context) : IWarehouseStore
{
    public async Task<Warehouse?> FindAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);

    public async Task<Warehouse?> FindForUpdateAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);

    public async Task<Warehouse?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        await context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Code == code, cancellationToken);

    public async Task<(IReadOnlyList<Warehouse> Items, int? TotalCount)> ListAsync(
        bool includeInactive, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Warehouses.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(w => w.Status == WarehouseStatus.Active);
        }

        query = query.OrderBy(w => w.Code);

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        return (await query.ToListAsync(cancellationToken), total);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken) =>
        await context.Warehouses.AddAsync(warehouse, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
