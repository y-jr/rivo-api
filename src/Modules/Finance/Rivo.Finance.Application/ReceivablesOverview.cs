using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;

namespace Rivo.Finance.Application;

/// <summary>
/// O contrato publicado de `finance`/AR para composição (Fase 8, ADR-041).
/// Primeiro consumidor previsto: o Dashboard Executivo — ainda por
/// construir, à espera deste e do lado de Contas a Pagar
/// (<see cref="PayablesOverview"/>).
/// </summary>
public sealed class ReceivablesOverview(ISalesInvoiceStore invoices, ICustomerDirectory customers)
    : IReceivablesOverview
{
    public async Task<decimal> GetNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        var facturado = await invoices.SumNetInvoicedAsync(from, to, currency, cancellationToken);
        var creditado = await invoices.SumNetCreditedAsync(from, to, currency, cancellationToken);

        return facturado - creditado;
    }

    public Task<decimal> GetOutstandingReceivablesAsync(string currency, CancellationToken cancellationToken) =>
        invoices.SumOutstandingAsync(currency, cancellationToken);

    public async Task<IReadOnlyList<CustomerRevenueView>> GetTopCustomersAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken)
    {
        var topo = await invoices.TopCustomersByInvoicedAsync(from, to, currency, count, cancellationToken);

        var vista = new List<CustomerRevenueView>(topo.Count);

        foreach (var entrada in topo)
        {
            var cliente = await customers.FindAsync(entrada.CustomerId, cancellationToken);

            // A vista mostra o nome de hoje, não o que a factura tinha
            // congelado — essa disciplina (BR-18) é para o documento
            // fiscal, não para um KPI de gestão. BR-14 impede eliminação
            // real, por isso `cliente` nulo seria falha de outro sítio,
            // nunca motivo esperado — mas a vista não rebenta por isso.
            vista.Add(new CustomerRevenueView(
                entrada.CustomerId, cliente?.Name ?? "Cliente desconhecido", entrada.NetTotal));
        }

        return vista;
    }
}
