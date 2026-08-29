using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// O saldo de uma factura: o que falta receber, e de onde vem esse número.
///
/// <para>
/// Devolve as parcelas e não só o total, porque "faltam 20 000" sem dizer se foi
/// creditado ou recebido é a resposta que obriga a ir procurar noutro sítio.
/// </para>
/// </summary>
public sealed class GetInvoiceBalance(ISalesInvoiceStore store)
{
    public async Task<InvoiceBalanceView?> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var factura = await store.FindAsync(invoiceId, cancellationToken);

        if (factura is null)
        {
            return null;
        }

        var emAberto = await store.OutstandingAsync(invoiceId, cancellationToken);
        var notas = await store.ListCreditNotesAsync(invoiceId, cancellationToken);

        var creditado = notas
            .Where(nota => nota.Status is InvoiceStatus.Normal)
            .Sum(nota => nota.GrossTotal);

        // Uma factura anulada não deve nada, e o total dela não conta para
        // dívida nenhuma.
        var facturado = factura.Status is InvoiceStatus.Cancelled ? 0m : factura.GrossTotal;

        return new InvoiceBalanceView(
            factura.Id,
            factura.Number.Formatted,
            factura.Status.ToString(),
            factura.Currency,
            facturado,
            creditado,
            // O recebido é o que sobra da conta: total − creditado − em aberto.
            // Deriva-se em vez de se somar outra vez, para que não possa
            // divergir do `OutstandingAsync`, que é a fonte.
            facturado - creditado - emAberto,
            emAberto,
            emAberto <= 0 && facturado > 0);
    }
}

/// <param name="Settled">Verdadeiro quando não falta receber nada.</param>
public sealed record InvoiceBalanceView(
    Guid InvoiceId,
    string Number,
    string Status,
    string Currency,
    decimal Invoiced,
    decimal Credited,
    decimal Received,
    decimal Outstanding,
    bool Settled);

public sealed class ListCreditNotes(ISalesInvoiceStore store)
{
    public async Task<IReadOnlyList<CreditNoteView>> ExecuteAsync(
        Guid? salesInvoiceId,
        CancellationToken cancellationToken)
    {
        var notas = await store.ListCreditNotesAsync(salesInvoiceId, cancellationToken);

        return [.. notas.Select(ToView)];
    }

    internal static CreditNoteView ToView(CreditNote nota) =>
        new(
            nota.Id,
            nota.Number.Formatted,
            nota.SalesInvoiceId,
            nota.CorrectedInvoiceNumber,
            nota.IssuedOn,
            nota.TaxPointDate,
            nota.Status.ToString(),
            nota.CustomerId,
            nota.Customer.Name,
            nota.Customer.TaxId,
            nota.Currency,
            nota.Reason,
            nota.NetTotal,
            nota.TaxTotal,
            nota.GrossTotal,
            nota.FiscalNotice,
            nota.CancelledAt,
            nota.CancellationReason,
            [.. nota.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new SalesInvoiceLineView(
                    line.LineNumber,
                    line.Description,
                    line.Quantity,
                    line.UnitPrice,
                    line.TaxCode,
                    line.TaxPercentage,
                    line.NetAmount,
                    line.TaxAmount))]);
}

public sealed record CreditNoteView(
    Guid CreditNoteId,
    string Number,
    Guid SalesInvoiceId,
    string CorrectedInvoiceNumber,
    DateOnly IssuedOn,
    DateOnly TaxPointDate,
    string Status,
    Guid? CustomerId,
    string CustomerName,
    string CustomerTaxId,
    string Currency,
    string Reason,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    string? FiscalNotice,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<SalesInvoiceLineView> Lines);

public sealed class GetCreditNote(ISalesInvoiceStore store)
{
    public async Task<CreditNoteView?> ExecuteAsync(Guid creditNoteId, CancellationToken cancellationToken)
    {
        var nota = await store.FindCreditNoteAsync(creditNoteId, cancellationToken);

        return nota is null ? null : ListCreditNotes.ToView(nota);
    }
}

public sealed class ListReceipts(ISalesInvoiceStore store)
{
    public async Task<IReadOnlyList<ReceiptView>> ExecuteAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var recibos = await store.ListReceiptsAsync(customerId, from, to, cancellationToken);

        return [.. recibos.Select(ToView)];
    }

    internal static ReceiptView ToView(Receipt recibo) =>
        new(
            recibo.Id,
            recibo.Number.Formatted,
            recibo.ReceivedOn,
            recibo.Status.ToString(),
            recibo.CustomerId,
            recibo.Customer.Name,
            recibo.Customer.TaxId,
            recibo.Currency,
            recibo.Method.ToString(),
            recibo.Total,
            recibo.Notes,
            recibo.FiscalNotice,
            recibo.CancelledAt,
            recibo.CancellationReason,
            [.. recibo.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new SettlementView(
                    line.LineNumber, line.SalesInvoiceId, line.InvoiceNumber, line.Amount))]);
}

public sealed record ReceiptView(
    Guid ReceiptId,
    string Number,
    DateOnly ReceivedOn,
    string Status,
    Guid? CustomerId,
    string CustomerName,
    string CustomerTaxId,
    string Currency,
    string Method,
    decimal Total,
    string? Notes,
    string? FiscalNotice,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<SettlementView> Settlements);

public sealed record SettlementView(
    int LineNumber,
    Guid SalesInvoiceId,
    string InvoiceNumber,
    decimal Amount);

public sealed class GetReceipt(ISalesInvoiceStore store)
{
    public async Task<ReceiptView?> ExecuteAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var recibo = await store.FindReceiptAsync(receiptId, cancellationToken);

        return recibo is null ? null : ListReceipts.ToView(recibo);
    }
}

/// <summary>
/// Anula uma nota de crédito — o crédito deixa de contar para o saldo.
///
/// <para>
/// Estorna na mesma unidade de trabalho, mesma disciplina de
/// <see cref="CancelSalesInvoice"/> — ver ali o comentário completo.
/// </para>
/// </summary>
public sealed class CancelCreditNote(
    ISalesInvoiceStore store, ReverseDocumentPosting reverse, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelInvoiceResult> ExecuteAsync(
        Guid creditNoteId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var nota = await store.FindCreditNoteForUpdateAsync(creditNoteId, cancellationToken);

        if (nota is null)
        {
            return CancelInvoiceResult.NotFound();
        }

        var agora = clock.GetUtcNow();

        try
        {
            nota.Cancel(reason, agora);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelInvoiceResult.Rejected(error.Message);
        }

        var estorno = await reverse.ReverseAsync(
            nota.Number.Formatted,
            $"Estorno de {nota.Number.Formatted}",
            DateOnly.FromDateTime(agora.UtcDateTime),
            agora,
            cancellationToken);

        if (estorno.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return CancelInvoiceResult.Rejected(estorno.Error!);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.CreditNoteCancelled,
                FinanceAuditEntityTypes.CreditNote,
                nota.Id.ToString(),
                context,
                PreviousValue: $$"""{"status":"Normal"}""",
                NewValue: $$"""{"status":"Cancelled","reason":"{{nota.CancellationReason}}"}"""),
            cancellationToken);

        return CancelInvoiceResult.Success();
    }
}

/// <summary>
/// Estorna um recebimento. A dívida volta a existir — é o que acontece quando um
/// cheque volta.
///
/// <para>
/// E estorna também na contabilidade, na mesma unidade de trabalho — mesma
/// disciplina de <see cref="CancelSalesInvoice"/>. Duas coisas chamadas
/// "estorno" aqui: a do documento (a dívida do cliente volta a existir) e a
/// do lançamento (o inverso do que o recebimento tinha posto nos livros).
/// </para>
/// </summary>
public sealed class CancelReceipt(
    ISalesInvoiceStore store, ReverseDocumentPosting reverse, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelInvoiceResult> ExecuteAsync(
        Guid receiptId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var recibo = await store.FindReceiptForUpdateAsync(receiptId, cancellationToken);

        if (recibo is null)
        {
            return CancelInvoiceResult.NotFound();
        }

        var agora = clock.GetUtcNow();

        try
        {
            recibo.Cancel(reason, agora);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelInvoiceResult.Rejected(error.Message);
        }

        var estorno = await reverse.ReverseAsync(
            recibo.Number.Formatted,
            $"Estorno de {recibo.Number.Formatted}",
            DateOnly.FromDateTime(agora.UtcDateTime),
            agora,
            cancellationToken);

        if (estorno.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return CancelInvoiceResult.Rejected(estorno.Error!);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.ReceiptCancelled,
                FinanceAuditEntityTypes.Receipt,
                recibo.Id.ToString(),
                context,
                PreviousValue: $$"""{"status":"Normal"}""",
                NewValue: $$"""{"status":"Cancelled","reason":"{{recibo.CancellationReason}}"}"""),
            cancellationToken);

        return CancelInvoiceResult.Success();
    }
}
