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

    public async Task<PurchaseOrder?> FindOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken) =>
        await context.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);

    public async Task<PurchaseOrder?> FindOrderForUpdateAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken) =>
        await context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken);

    public async Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(
        Guid? requisitionId,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        var query = context.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .AsQueryable();

        if (requisitionId is { } requisicao)
        {
            query = query.Where(o => o.RequisitionId == requisicao);
        }

        if (supplierId is { } fornecedor)
        {
            query = query.Where(o => o.SupplierId == fornecedor);
        }

        return await query
            .OrderByDescending(o => o.IssuedOn)
            .ThenByDescending(o => o.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> OrderedAgainstRequisitionAsync(
        Guid requisitionId,
        CancellationToken cancellationToken)
    {
        // A soma é feita na base de dados e não em memória: carregar todas as
        // ordens para as somar aqui traria também todas as linhas de todas
        // elas, e o número que interessa é um só.
        //
        // `SumAsync` sobre um conjunto vazio devolveria zero na mesma, mas o
        // `decimal?` está aqui de propósito — sem ele, a tradução para SQL
        // devolve NULL quando não há linhas e o EF Core rebenta ao materializar.
        var total = await context.Orders
            .AsNoTracking()
            .Where(o => o.RequisitionId == requisitionId && o.Status == PurchaseOrderStatus.Issued)
            .SelectMany(o => o.Lines)
            .SumAsync(l => (decimal?)(l.Quantity * l.UnitPrice), cancellationToken);

        return total ?? 0m;
    }

    public async Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken) =>
        await context.Orders.AddAsync(order, cancellationToken);

    public async Task<GoodsReceipt?> FindReceiptAsync(
        Guid goodsReceiptId,
        CancellationToken cancellationToken) =>
        await context.Receipts
            .AsNoTracking()
            .Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == goodsReceiptId, cancellationToken);

    public async Task<GoodsReceipt?> FindReceiptForUpdateAsync(
        Guid goodsReceiptId,
        CancellationToken cancellationToken) =>
        await context.Receipts
            .Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == goodsReceiptId, cancellationToken);

    public async Task<IReadOnlyList<GoodsReceipt>> ListReceiptsAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var query = context.Receipts
            .AsNoTracking()
            .Include(g => g.Lines)
            .AsQueryable();

        if (purchaseOrderId is { } ordem)
        {
            query = query.Where(g => g.PurchaseOrderId == ordem);
        }

        return await query
            .OrderByDescending(g => g.ReceivedOn)
            .ThenByDescending(g => g.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> ReceivedByOrderLineAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        // Agrupado na base de dados. Trazer as recepções todas para somar aqui
        // cresceria com o histórico da ordem, e o que interessa é um número por
        // linha.
        var somas = await context.Receipts
            .AsNoTracking()
            .Where(g => g.PurchaseOrderId == purchaseOrderId
                && g.Status == GoodsReceiptStatus.Registered)
            .SelectMany(g => g.Lines)
            .GroupBy(l => l.PurchaseOrderLineId)
            .Select(grupo => new
            {
                LinhaDaOrdem = grupo.Key,
                Recebido = grupo.Sum(l => l.QuantityReceived),
            })
            .ToListAsync(cancellationToken);

        return somas.ToDictionary(s => s.LinhaDaOrdem, s => s.Recebido);
    }

    public async Task<bool> HasReceiptsAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        await context.Receipts
            .AsNoTracking()
            .AnyAsync(
                g => g.PurchaseOrderId == purchaseOrderId
                    && g.Status == GoodsReceiptStatus.Registered,
                cancellationToken);

    public async Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken) =>
        await context.Receipts.AddAsync(receipt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
