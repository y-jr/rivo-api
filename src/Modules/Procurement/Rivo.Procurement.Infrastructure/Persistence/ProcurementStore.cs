using Microsoft.EntityFrameworkCore;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Infrastructure.Persistence;

public sealed class ProcurementStore(ProcurementDbContext context) : IProcurementStore
{
    public async Task<Supplier?> FindSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

    public async Task<Supplier?> FindSupplierForUpdateAsync(Guid supplierId, CancellationToken cancellationToken) =>
        await context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

    public async Task<Supplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken cancellationToken) =>
        await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TaxId == taxId, cancellationToken);

    public async Task<IReadOnlyList<Supplier>> ListSuppliersAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.Suppliers.AsNoTracking().AsQueryable();

        // Por omissão só os activos: quem lista para comprar não quer ver
        // fornecedores desqualificados no meio.
        if (!includeInactive)
        {
            query = query.Where(s => s.Status == SupplierStatus.Active);
        }

        return await query.OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken) =>
        await context.Suppliers.AddAsync(supplier, cancellationToken);

    public async Task<PurchaseRequisition?> FindRequisitionAsync(
        Guid requisitionId,
        CancellationToken cancellationToken) =>
        await context.Requisitions
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId, cancellationToken);

    public async Task<PurchaseRequisition?> FindRequisitionForUpdateAsync(
        Guid requisitionId,
        CancellationToken cancellationToken) =>
        await context.Requisitions
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == requisitionId, cancellationToken);

    public async Task<IReadOnlyList<PurchaseRequisition>> ListRequisitionsAsync(
        Guid? requestedByEmployeeId,
        RequisitionStatus? status,
        CancellationToken cancellationToken)
    {
        var query = context.Requisitions
            .AsNoTracking()
            .Include(r => r.Lines)
            .AsQueryable();

        if (requestedByEmployeeId is { } requisitante)
        {
            query = query.Where(r => r.RequestedByEmployeeId == requisitante);
        }

        if (status is { } estado)
        {
            query = query.Where(r => r.Status == estado);
        }

        // Mais recentes primeiro: quem abre a lista quer ver o que está em
        // curso, não o que se pediu há dois anos.
        return await query
            .OrderByDescending(r => r.RequestedOn)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRequisitionAsync(
        PurchaseRequisition requisition,
        CancellationToken cancellationToken) =>
        await context.Requisitions.AddAsync(requisition, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
