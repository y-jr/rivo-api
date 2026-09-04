using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

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

    public async Task<decimal> GetCustomerNetRevenueAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        var facturado = await invoices.SumNetInvoicedForCustomerAsync(customerId, from, to, currency, cancellationToken);
        var creditado = await invoices.SumNetCreditedForCustomerAsync(customerId, from, to, currency, cancellationToken);

        return facturado - creditado;
    }

    public Task<decimal> GetCustomerOutstandingAsync(
        Guid customerId, string currency, CancellationToken cancellationToken) =>
        invoices.SumOutstandingForCustomerAsync(customerId, currency, cancellationToken);

    public async Task<IReadOnlyList<CustomerInvoiceView>> ListCustomerInvoicesAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var facturas = await invoices.ListAsync(customerId, from: null, to: null, cancellationToken);

        return [.. facturas.Select(factura => new CustomerInvoiceView(
            factura.Id,
            factura.Number.Formatted,
            factura.IssuedOn,
            factura.Status.ToString(),
            factura.Currency,
            factura.GrossTotal))];
    }

    public async Task<CustomerStatementView> GetCustomerStatementAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        // Tudo até `to`, sem limite inferior: o que cai antes de `from`
        // financia a abertura, o resto vira movimento do período. Duas
        // idas à base a mais por chamada seria o preço de as separar já
        // filtradas — sem consumidor a queixar-se da diferença, fica assim.
        var facturas = (await invoices.ListAsync(customerId, from: null, to, cancellationToken))
            .Where(i => i.Status == InvoiceStatus.Normal && i.Currency == currency)
            .ToList();

        var notas = (await invoices.ListCreditNotesForCustomerAsync(customerId, from: null, to, cancellationToken))
            .Where(n => n.Status == InvoiceStatus.Normal && n.Currency == currency)
            .ToList();

        var recibos = (await invoices.ListReceiptsAsync(customerId, from: null, to, cancellationToken))
            .Where(r => r.Status == InvoiceStatus.Normal && r.Currency == currency)
            .ToList();

        var abertura =
            facturas.Where(i => i.IssuedOn < from).Sum(i => i.GrossTotal)
            - notas.Where(n => n.IssuedOn < from).Sum(n => n.GrossTotal)
            - recibos.Where(r => r.ReceivedOn < from).Sum(r => r.Lines.Sum(l => l.Amount));

        var brutos = new List<(DateOnly Data, string Tipo, string Numero, string Sentido, decimal Valor)>();

        brutos.AddRange(facturas
            .Where(i => i.IssuedOn >= from)
            .Select(i => (i.IssuedOn, "Factura", i.Number.Formatted, "Debit", i.GrossTotal)));

        brutos.AddRange(notas
            .Where(n => n.IssuedOn >= from)
            .Select(n => (n.IssuedOn, "NotaCredito", n.Number.Formatted, "Credit", n.GrossTotal)));

        brutos.AddRange(recibos
            .Where(r => r.ReceivedOn >= from)
            .Select(r => (r.ReceivedOn, "Recibo", r.Number.Formatted, "Credit", r.Lines.Sum(l => l.Amount))));

        var saldo = abertura;
        var linhas = new List<CustomerStatementLine>(brutos.Count);

        foreach (var movimento in brutos.OrderBy(m => m.Data))
        {
            saldo += movimento.Sentido == "Debit" ? movimento.Valor : -movimento.Valor;
            linhas.Add(new CustomerStatementLine(
                movimento.Data, movimento.Tipo, movimento.Numero, movimento.Sentido, movimento.Valor, saldo));
        }

        return new CustomerStatementView(abertura, linhas, saldo);
    }

    public async Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        var pontos = new List<MonthlyAmount>();

        foreach (var (ano, mes, inicio, fim) in MonthlyWindows.Enumerate(from, to))
        {
            var facturado = await invoices.SumNetInvoicedAsync(inicio, fim, currency, cancellationToken);
            var creditado = await invoices.SumNetCreditedAsync(inicio, fim, currency, cancellationToken);

            pontos.Add(new MonthlyAmount(ano, mes, facturado - creditado));
        }

        return pontos;
    }
}
