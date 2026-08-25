using Microsoft.EntityFrameworkCore;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class PayablesStore(FinanceDbContext context) : IPayablesStore
{
    public async Task<BankAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public async Task<BankAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken) =>
        // Rastreada: é aqui que a contenção acontece. O contador de
        // concorrência desta linha é o que faz dois pagamentos simultâneos
        // colidirem em vez de passarem os dois com o mesmo saldo lido (BR-17).
        await context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public async Task<IReadOnlyList<BankAccount>> ListAccountsAsync(
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var query = context.Accounts.AsNoTracking().AsQueryable();

        if (!includeClosed)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query.OrderBy(a => a.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAccountAsync(BankAccount account, CancellationToken cancellationToken) =>
        await context.Accounts.AddAsync(account, cancellationToken);

    public async Task<PurchaseInvoice?> FindPurchaseInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        await context.PurchaseInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

    public async Task<PurchaseInvoice?> FindPurchaseInvoiceForUpdateAsync(
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        await context.PurchaseInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

    public async Task<IReadOnlyList<PurchaseInvoice>> ListPurchaseInvoicesAsync(
        DateOnly? dueBefore,
        CancellationToken cancellationToken)
    {
        var query = context.PurchaseInvoices.AsNoTracking().AsQueryable();

        if (dueBefore is { } limite)
        {
            query = query.Where(i => i.DueOn <= limite);
        }

        // Por vencimento: é a ordem da fila de pagamentos.
        return await query.OrderBy(i => i.DueOn).ToListAsync(cancellationToken);
    }

    public Task<bool> PurchaseInvoiceExistsAsync(
        string supplierTaxId,
        string supplierInvoiceNumber,
        CancellationToken cancellationToken) =>
        context.PurchaseInvoices.AnyAsync(
            i => i.SupplierTaxId == supplierTaxId
                && i.SupplierInvoiceNumber == supplierInvoiceNumber,
            cancellationToken);

    public async Task AddPurchaseInvoiceAsync(PurchaseInvoice invoice, CancellationToken cancellationToken) =>
        await context.PurchaseInvoices.AddAsync(invoice, cancellationToken);

    public async Task<PaymentRequest?> FindPaymentRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        await context.PaymentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public async Task<PaymentRequest?> FindPaymentRequestForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken) =>
        await context.PaymentRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public async Task<IReadOnlyList<PaymentRequest>> ListPaymentRequestsAsync(
        Guid? purchaseInvoiceId,
        CancellationToken cancellationToken)
    {
        var query = context.PaymentRequests.AsNoTracking().AsQueryable();

        if (purchaseInvoiceId is { } factura)
        {
            query = query.Where(r => r.PurchaseInvoiceId == factura);
        }

        return await query.OrderByDescending(r => r.RequestedOn).ToListAsync(cancellationToken);
    }

    public async Task<decimal> CommittedAsync(Guid purchaseInvoiceId, CancellationToken cancellationToken) =>
        // Cancelados não contam: um pedido cancelado libertou o valor que
        // reservava.
        await context.PaymentRequests
            .AsNoTracking()
            .Where(r => r.PurchaseInvoiceId == purchaseInvoiceId
                && r.Status != PaymentRequestStatus.Cancelled)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    public async Task AddPaymentRequestAsync(PaymentRequest request, CancellationToken cancellationToken) =>
        await context.PaymentRequests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
