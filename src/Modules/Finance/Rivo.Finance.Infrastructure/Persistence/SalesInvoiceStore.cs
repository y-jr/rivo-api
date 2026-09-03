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

    /// <summary>
    /// Total − creditado − recebido, contando só documentos **não anulados**.
    ///
    /// <para>
    /// Três consultas em vez de um `join`, de propósito: um `join` entre notas e
    /// recibos multiplicaria as linhas quando houvesse mais do que uma de cada,
    /// e o total sairia inflacionado. É o erro clássico de somar sobre um
    /// produto cartesiano, e não dá sinal — só um número errado.
    /// </para>
    /// </summary>
    public async Task<decimal> OutstandingAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var factura = await context.Invoices
            .AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { i.GrossTotal, i.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (factura is null || factura.Status is InvoiceStatus.Cancelled)
        {
            // Uma factura anulada não deve nada. Devolver o total dela poria-a a
            // aceitar recebimentos.
            return 0m;
        }

        var creditado = await context.CreditNotes
            .AsNoTracking()
            .Where(n => n.SalesInvoiceId == invoiceId && n.Status == InvoiceStatus.Normal)
            .SumAsync(n => (decimal?)n.GrossTotal, cancellationToken) ?? 0m;

        var recebido = await context.Receipts
            .AsNoTracking()
            .Where(r => r.Status == InvoiceStatus.Normal)
            .SelectMany(r => r.Lines)
            .Where(l => l.SalesInvoiceId == invoiceId)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        return factura.GrossTotal - creditado - recebido;
    }

    /// <summary>
    /// A mesma conta de <see cref="OutstandingAsync"/>, sobre o conjunto —
    /// três agregações (facturado, creditado, recebido), mesma razão de
    /// evitar `join` que a fica documentada ali.
    /// </summary>
    public async Task<decimal> SumOutstandingAsync(string currency, CancellationToken cancellationToken)
    {
        var facturado = await context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Normal && i.Currency == currency)
            .SumAsync(i => (decimal?)i.GrossTotal, cancellationToken) ?? 0m;

        var creditado = await context.CreditNotes
            .AsNoTracking()
            .Where(n => n.Status == InvoiceStatus.Normal && n.Currency == currency)
            .SumAsync(n => (decimal?)n.GrossTotal, cancellationToken) ?? 0m;

        var recebido = await context.Receipts
            .AsNoTracking()
            .Where(r => r.Status == InvoiceStatus.Normal && r.Currency == currency)
            .SelectMany(r => r.Lines)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        return facturado - creditado - recebido;
    }

    public async Task<decimal> SumNetInvoicedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        await context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Normal
                && i.Currency == currency
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .SumAsync(i => (decimal?)i.NetTotal, cancellationToken) ?? 0m;

    public async Task<decimal> SumNetCreditedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        await context.CreditNotes
            .AsNoTracking()
            .Where(n => n.Status == InvoiceStatus.Normal
                && n.Currency == currency
                && n.IssuedOn >= from
                && n.IssuedOn <= to)
            .SumAsync(n => (decimal?)n.NetTotal, cancellationToken) ?? 0m;

    public async Task<decimal> SumOutstandingForCustomerAsync(
        Guid customerId, string currency, CancellationToken cancellationToken)
    {
        var facturado = await context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Normal && i.Currency == currency && i.CustomerId == customerId)
            .SumAsync(i => (decimal?)i.GrossTotal, cancellationToken) ?? 0m;

        var creditado = await context.CreditNotes
            .AsNoTracking()
            .Where(n => n.Status == InvoiceStatus.Normal && n.Currency == currency && n.CustomerId == customerId)
            .SumAsync(n => (decimal?)n.GrossTotal, cancellationToken) ?? 0m;

        var recebido = await context.Receipts
            .AsNoTracking()
            .Where(r => r.Status == InvoiceStatus.Normal && r.Currency == currency && r.CustomerId == customerId)
            .SelectMany(r => r.Lines)
            .SumAsync(l => (decimal?)l.Amount, cancellationToken) ?? 0m;

        return facturado - creditado - recebido;
    }

    public async Task<decimal> SumNetInvoicedForCustomerAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        await context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Normal
                && i.Currency == currency
                && i.CustomerId == customerId
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .SumAsync(i => (decimal?)i.NetTotal, cancellationToken) ?? 0m;

    public async Task<decimal> SumNetCreditedForCustomerAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        await context.CreditNotes
            .AsNoTracking()
            .Where(n => n.Status == InvoiceStatus.Normal
                && n.Currency == currency
                && n.CustomerId == customerId
                && n.IssuedOn >= from
                && n.IssuedOn <= to)
            .SumAsync(n => (decimal?)n.NetTotal, cancellationToken) ?? 0m;

    public async Task<IReadOnlyList<CustomerInvoicedTotal>> TopCustomersByInvoicedAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken)
    {
        // O SQL Server traduz `GroupBy` seguido de projecção para um tipo
        // anónimo sem problema — para um registo (construtor posicional)
        // já não, e o EF Core recusa-se a inventar client evaluation
        // silencioso. Projecta-se para o tipo anónimo primeiro, e só depois
        // de materializado é que vira `CustomerInvoicedTotal`.
        var agregados = await context.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Normal
                && i.Currency == currency
                && i.CustomerId != null
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .GroupBy(i => i.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, NetTotal = g.Sum(i => i.NetTotal) })
            .OrderByDescending(c => c.NetTotal)
            .Take(count)
            .ToListAsync(cancellationToken);

        return [.. agregados.Select(a => new CustomerInvoicedTotal(a.CustomerId, a.NetTotal))];
    }

    public async Task<CreditNote?> FindCreditNoteAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        await context.CreditNotes
            .AsNoTracking()
            .Include(n => n.Lines)
            .FirstOrDefaultAsync(n => n.Id == creditNoteId, cancellationToken);

    public async Task<CreditNote?> FindCreditNoteForUpdateAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        await context.CreditNotes
            .Include(n => n.Lines)
            .FirstOrDefaultAsync(n => n.Id == creditNoteId, cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> ListCreditNotesAsync(
        Guid? salesInvoiceId,
        CancellationToken cancellationToken)
    {
        var query = context.CreditNotes.AsNoTracking().Include(n => n.Lines).AsQueryable();

        if (salesInvoiceId is { } factura)
        {
            query = query.Where(n => n.SalesInvoiceId == factura);
        }

        return await query.OrderByDescending(n => n.IssuedOn).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CreditNote>> ListCreditNotesForCustomerAsync(
        Guid customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = context.CreditNotes
            .AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .AsQueryable();

        if (from is { } inicio)
        {
            query = query.Where(n => n.IssuedOn >= inicio);
        }

        if (to is { } fim)
        {
            query = query.Where(n => n.IssuedOn <= fim);
        }

        return await query.OrderBy(n => n.IssuedOn).ToListAsync(cancellationToken);
    }

    public async Task AddCreditNoteAsync(CreditNote note, CancellationToken cancellationToken) =>
        await context.CreditNotes.AddAsync(note, cancellationToken);

    public async Task<Receipt?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
        await context.Receipts
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

    public async Task<Receipt?> FindReceiptForUpdateAsync(Guid receiptId, CancellationToken cancellationToken) =>
        await context.Receipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

    public async Task<IReadOnlyList<Receipt>> ListReceiptsAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = context.Receipts.AsNoTracking().Include(r => r.Lines).AsQueryable();

        if (customerId is { } cliente)
        {
            query = query.Where(r => r.CustomerId == cliente);
        }

        if (from is { } inicio)
        {
            query = query.Where(r => r.ReceivedOn >= inicio);
        }

        if (to is { } fim)
        {
            query = query.Where(r => r.ReceivedOn <= fim);
        }

        return await query.OrderByDescending(r => r.ReceivedOn).ToListAsync(cancellationToken);
    }

    public async Task AddReceiptAsync(Receipt receipt, CancellationToken cancellationToken) =>
        await context.Receipts.AddAsync(receipt, cancellationToken);

    public async Task<PaymentClaim?> FindPaymentClaimAsync(Guid claimId, CancellationToken cancellationToken) =>
        await context.PaymentClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);

    public async Task<PaymentClaim?> FindPaymentClaimForUpdateAsync(Guid claimId, CancellationToken cancellationToken) =>
        await context.PaymentClaims
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);

    public async Task<IReadOnlyList<PaymentClaim>> ListPaymentClaimsAsync(
        Guid? customerId,
        PaymentClaimStatus? status,
        CancellationToken cancellationToken)
    {
        var query = context.PaymentClaims.AsNoTracking().AsQueryable();

        if (customerId is { } cliente)
        {
            query = query.Where(c => c.CustomerId == cliente);
        }

        if (status is { } estado)
        {
            query = query.Where(c => c.Status == estado);
        }

        return await query.OrderByDescending(c => c.SubmittedAt).ToListAsync(cancellationToken);
    }

    public async Task AddPaymentClaimAsync(PaymentClaim claim, CancellationToken cancellationToken) =>
        await context.PaymentClaims.AddAsync(claim, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
