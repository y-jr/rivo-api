using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

public sealed class ListSalesInvoices(ISalesInvoiceStore store)
{
    public async Task<IReadOnlyList<SalesInvoiceSummary>> ExecuteAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var facturas = await store.ListAsync(customerId, from, to, cancellationToken);

        return [.. facturas.Select(invoice => new SalesInvoiceSummary(
            invoice.Id,
            invoice.Number.Formatted,
            invoice.IssuedOn,
            invoice.Status.ToString(),
            invoice.Customer.Name,
            invoice.Customer.TaxId,
            invoice.Currency,
            invoice.GrossTotal))];
    }
}

public sealed record SalesInvoiceSummary(
    Guid InvoiceId,
    string Number,
    DateOnly IssuedOn,
    string Status,
    string CustomerName,
    string CustomerTaxId,
    string Currency,
    decimal GrossTotal);

public sealed class GetSalesInvoice(ISalesInvoiceStore store)
{
    public async Task<SalesInvoiceView?> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var factura = await store.FindAsync(invoiceId, cancellationToken);

        return factura is null ? null : ToView(factura);
    }

    private static SalesInvoiceView ToView(SalesInvoice invoice) =>
        new(
            invoice.Id,
            invoice.Number.Formatted,
            invoice.IssuedOn,
            invoice.TaxPointDate,
            invoice.Status.ToString(),
            invoice.CustomerId,
            invoice.Customer.IsFinalConsumer,
            invoice.Customer.Name,
            invoice.Customer.TaxId,
            invoice.Customer.AddressDetail,
            invoice.Customer.City,
            invoice.Customer.Country,
            invoice.Currency,
            invoice.NetTotal,
            invoice.TaxTotal,
            invoice.GrossTotal,
            invoice.FiscalNotice,
            invoice.CancelledAt,
            invoice.CancellationReason,
            [.. invoice.Lines
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

/// <param name="CustomerId">Nulo numa venda a consumidor final.</param>
/// <param name="IsFinalConsumer">
/// Verdadeiro quando a venda foi a quem não se identificou. Distingue-se de um
/// cliente com morada em falta: aqui a morada está vazia porque não existe.
/// </param>
/// <param name="FiscalNotice">
/// Menção de não-validade fiscal, congelada na emissão (ADR-036). Nula só num
/// sistema certificado. <strong>Quem apresentar a factura tem de a mostrar.</strong>
/// </param>
public sealed record SalesInvoiceView(
    Guid InvoiceId,
    string Number,
    DateOnly IssuedOn,
    DateOnly TaxPointDate,
    string Status,
    Guid? CustomerId,
    bool IsFinalConsumer,
    string CustomerName,
    string CustomerTaxId,
    string CustomerAddressDetail,
    string CustomerCity,
    string CustomerCountry,
    string Currency,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    string? FiscalNotice,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<SalesInvoiceLineView> Lines);

public sealed record SalesInvoiceLineView(
    int LineNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode,
    decimal TaxPercentage,
    decimal NetAmount,
    decimal TaxAmount);

/// <summary>
/// Anula uma factura emitida. É a única alteração possível a um documento
/// fiscal — não há eliminação (BR-14).
/// </summary>
public sealed class CancelSalesInvoice(ISalesInvoiceStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelInvoiceResult> ExecuteAsync(
        Guid invoiceId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var factura = await store.FindForUpdateAsync(invoiceId, cancellationToken);

        if (factura is null)
        {
            return CancelInvoiceResult.NotFound();
        }

        try
        {
            factura.Cancel(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelInvoiceResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        // Auditada com o motivo: anular é a operação mais sensível que existe
        // sobre um documento emitido, e o motivo é o que fica para quem
        // conferir depois.
        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.InvoiceCancelled,
                FinanceAuditEntityTypes.SalesInvoice,
                factura.Id.ToString(),
                context,
                PreviousValue: $$"""{"status":"Normal"}""",
                NewValue: $$"""{"status":"Cancelled","reason":"{{factura.CancellationReason}}"}"""),
            cancellationToken);

        return CancelInvoiceResult.Success();
    }
}

public sealed record CancelInvoiceResult(CancelInvoiceOutcome Outcome, string? Error)
{
    public static CancelInvoiceResult Success() => new(CancelInvoiceOutcome.Cancelled, null);

    public static CancelInvoiceResult NotFound() => new(CancelInvoiceOutcome.NotFound, null);

    public static CancelInvoiceResult Rejected(string error) => new(CancelInvoiceOutcome.Rejected, error);
}

public enum CancelInvoiceOutcome
{
    Cancelled,
    NotFound,
    Rejected,
}

public sealed class ListDocumentSeries(ISalesInvoiceStore store)
{
    public async Task<IReadOnlyList<DocumentSeriesView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var series = await store.ListSeriesAsync(cancellationToken);

        return [.. series.Select(s => new DocumentSeriesView(
            s.Id, s.Type.ToString(), s.Code, s.NextSequence, s.IsActive))];
    }
}

public sealed record DocumentSeriesView(
    Guid SeriesId,
    string Type,
    string Code,
    int NextSequence,
    bool IsActive);

public sealed class OpenDocumentSeries(ISalesInvoiceStore store, IAuditTrail audit)
{
    public async Task<OpenSeriesResult> ExecuteAsync(
        string code,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var normalizado = (code ?? string.Empty).Trim().ToUpperInvariant();

        // Duas séries com o mesmo código dariam dois documentos com o mesmo
        // número. O índice único é a segunda linha; esta é a primeira.
        if (await store.SeriesExistsAsync(DocumentType.FT, normalizado, cancellationToken))
        {
            return OpenSeriesResult.Duplicate();
        }

        DocumentSeries serie;

        try
        {
            serie = DocumentSeries.Open(DocumentType.FT, normalizado);
        }
        catch (ArgumentException error)
        {
            return OpenSeriesResult.Rejected(error.Message);
        }

        await store.AddSeriesAsync(serie, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.SeriesOpened,
                FinanceAuditEntityTypes.DocumentSeries,
                serie.Id.ToString(),
                context,
                NewValue: $$"""{"type":"FT","code":"{{serie.Code}}"}"""),
            cancellationToken);

        return OpenSeriesResult.Success(serie.Id);
    }
}

public sealed record OpenSeriesResult(OpenSeriesOutcome Outcome, Guid? SeriesId, string? Error)
{
    public static OpenSeriesResult Success(Guid seriesId) => new(OpenSeriesOutcome.Opened, seriesId, null);

    public static OpenSeriesResult Duplicate() => new(OpenSeriesOutcome.Duplicate, null, null);

    public static OpenSeriesResult Rejected(string error) => new(OpenSeriesOutcome.Rejected, null, error);
}

public enum OpenSeriesOutcome
{
    Opened,
    Duplicate,
    Rejected,
}
