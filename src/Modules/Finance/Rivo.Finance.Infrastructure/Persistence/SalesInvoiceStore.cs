using Microsoft.EntityFrameworkCore;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class SalesInvoiceStore(FinanceDbContext context) : ISalesInvoiceStore
{
    public async Task<DocumentSeries?> FindSeriesForAllocationAsync(
        DocumentType type,
        string code,
        CancellationToken cancellationToken) =>
        // Rastreada de propósito: o avanço do contador tem de entrar na mesma
        // transacção da factura, e o token de concorrência desta linha é o que
        // faz duas emissões simultâneas colidirem.
        await context.Series
            .FirstOrDefaultAsync(s => s.Type == type && s.Code == code, cancellationToken);

    public async Task<IReadOnlyList<DocumentSeries>> ListSeriesAsync(CancellationToken cancellationToken) =>
        await context.Series
            .AsNoTracking()
            .OrderBy(s => s.Type)
            .ThenBy(s => s.Code)
            .ToListAsync(cancellationToken);

    public async Task AddSeriesAsync(DocumentSeries series, CancellationToken cancellationToken) =>
        await context.Series.AddAsync(series, cancellationToken);

    public Task<bool> SeriesExistsAsync(
        DocumentType type,
        string code,
        CancellationToken cancellationToken) =>
        context.Series.AnyAsync(s => s.Type == type && s.Code == code, cancellationToken);

    public async Task<SalesInvoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await context.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

    public async Task<SalesInvoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await context.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

    public async Task<IReadOnlyList<SalesInvoice>> ListAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = context.Invoices.AsNoTracking().Include(i => i.Lines).AsQueryable();

        if (customerId is { } cliente)
        {
            query = query.Where(i => i.CustomerId == cliente);
        }

        if (from is { } inicio)
        {
            query = query.Where(i => i.IssuedOn >= inicio);
        }

        if (to is { } fim)
        {
            query = query.Where(i => i.IssuedOn <= fim);
        }

        // Anuladas incluídas de propósito: continuam a existir e a contar para
        // a sequência (BR-14). Esconder uma factura anulada faria a numeração
        // parecer ter buracos.
        return await query
            .OrderByDescending(i => i.IssuedOn)
            .ThenByDescending(i => i.Number.Sequence)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken) =>
        await context.Invoices.AddAsync(invoice, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
