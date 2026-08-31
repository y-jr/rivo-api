using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;

namespace Rivo.Finance.Application;

/// <summary>
/// O contrato publicado de `finance`/Contas a Pagar para composição (Fase
/// 8, ADR-041) — separado de <see cref="ReceivablesOverview"/> pela mesma
/// razão que <see cref="IPayablesStore"/> é separado de
/// <see cref="ISalesInvoiceStore"/>.
/// </summary>
public sealed class PayablesOverview(IPayablesStore payables) : IPayablesOverview
{
    public Task<decimal> GetNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        payables.SumNetExpensesAsync(from, to, currency, cancellationToken);

    public Task<decimal> GetOutstandingPayablesAsync(string currency, CancellationToken cancellationToken) =>
        payables.SumOutstandingPayablesAsync(currency, cancellationToken);
}
