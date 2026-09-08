using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application;

/// <summary>
/// Traduz as facturas de venda para o contrato de relato — mesmo desenho de
/// <see cref="ReceivablesOverview"/>: o modelo interno não sai (ADR-010).
/// </summary>
public sealed class SalesInvoiceReporting(ISalesInvoiceStore store) : ISalesInvoiceReporting
{
    public async Task<IReadOnlyList<ReportedInvoice>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // `customerId: null` — todas. As anuladas vêm incluídas: o `ListAsync`
        // não filtra por estado, e é isso que se quer (ver o contrato).
        var facturas = await store.ListAsync(null, from, to, cancellationToken);

        return [.. facturas.Select(Traduzir)];
    }

    private static ReportedInvoice Traduzir(SalesInvoice factura) =>
        new(
            factura.Number.Formatted,
            factura.Number.Type.ToString(),
            factura.IssuedOn,
            factura.TaxPointDate,
            factura.SystemEntryDate,

            // Quando o estado actual foi fixado. Para uma factura viva é o
            // momento do registo; para uma anulada é a anulação — e é essa a
            // data que o SAF-T quer em `InvoiceStatusDate`, não a de emissão.
            factura.CancelledAt ?? factura.SystemEntryDate,
            factura.Status is InvoiceStatus.Cancelled,
            factura.CancellationReason,
            factura.CustomerId,

            // O cliente congelado na emissao, e nao o de hoje. Uma venda a
            // consumidor final nao tem registo em `commercial`, e e daqui que
            // a exportacao tira o que precisa para o declarar no ficheiro.
            factura.Customer.Name,
            factura.Customer.TaxId,
            factura.Customer.AddressDetail,
            factura.Customer.City,
            factura.Customer.Country,
            factura.IssuedByUserId,
            factura.Hash,
            factura.Currency,
            factura.NetTotal,
            factura.TaxTotal,
            factura.GrossTotal,
            [.. factura.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new ReportedInvoiceLine(
                    l.LineNumber,
                    l.ProductCode,
                    l.Description,
                    l.Quantity,
                    l.UnitOfMeasure,
                    l.UnitPrice,
                    l.NetAmount,
                    l.TaxCode,
                    l.TaxPercentage))]);
}
