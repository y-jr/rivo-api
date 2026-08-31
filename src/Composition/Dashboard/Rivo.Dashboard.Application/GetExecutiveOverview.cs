using Rivo.Finance.Contracts;

namespace Rivo.Dashboard.Application;

/// <summary>
/// O Dashboard Executivo (Fase 8) — os cinco números que o utilizador
/// pediu, num só lugar: receita, despesa, lucro, o que falta receber, o
/// que falta pagar, mais os clientes que mais facturaram no período.
///
/// <para>
/// <strong>Camada de composição, não módulo.</strong> Não possui dados
/// próprios: lê `finance` pelos seus contratos publicados
/// (<see cref="IReceivablesOverview"/>, <see cref="IPayablesOverview"/>,
/// ADR-041) e nada aqui altera nenhum dos dois.
/// </para>
///
/// <para>
/// <strong>Lucro é <c>Receita − Despesa</c>, calculado aqui, não um
/// contrato à parte.</strong> Os dois lados já vêm no mesmo regime — de
/// compromisso, e simétricos de propósito (`modules/finance.md`) — por
/// isso subtrair os dois números já publicados é a conta inteira. Não é
/// lucro contabilístico (não há plano de contas carregado, PGC por fazer)
/// — é a leitura honesta do que os documentos emitidos e registados dizem
/// para o período, nada mais.
/// </para>
/// </summary>
public sealed class GetExecutiveOverview(IReceivablesOverview receivables, IPayablesOverview payables)
{
    public async Task<ExecutiveOverviewResult> ExecuteAsync(
        DateOnly from,
        DateOnly to,
        string currency,
        int topCustomersCount,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return ExecutiveOverviewResult.Rejected("A data inicial não pode ser posterior à data final.");
        }

        var receita = await receivables.GetNetRevenueAsync(from, to, currency, cancellationToken);
        var despesa = await payables.GetNetExpensesAsync(from, to, currency, cancellationToken);
        var aReceber = await receivables.GetOutstandingReceivablesAsync(currency, cancellationToken);
        var aPagar = await payables.GetOutstandingPayablesAsync(currency, cancellationToken);
        var topClientes = await receivables.GetTopCustomersAsync(
            from, to, currency, topCustomersCount, cancellationToken);

        return ExecutiveOverviewResult.Success(new ExecutiveOverviewView(
            from, to, currency, receita, despesa, receita - despesa, aReceber, aPagar, topClientes));
    }
}

/// <param name="Receivables">Saldo corrente de Contas a Receber — não uma data passada (ver o contrato).</param>
/// <param name="Payables">Saldo corrente de Contas a Pagar, pela mesma razão.</param>
public sealed record ExecutiveOverviewView(
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal Revenue,
    decimal Expenses,
    decimal Profit,
    decimal Receivables,
    decimal Payables,
    IReadOnlyList<CustomerRevenueView> TopCustomers);

public sealed record ExecutiveOverviewResult(
    ExecutiveOverviewOutcome Outcome, ExecutiveOverviewView? Overview, string? Error)
{
    public static ExecutiveOverviewResult Success(ExecutiveOverviewView overview) =>
        new(ExecutiveOverviewOutcome.Computed, overview, null);

    public static ExecutiveOverviewResult Rejected(string error) =>
        new(ExecutiveOverviewOutcome.Rejected, null, error);
}

public enum ExecutiveOverviewOutcome
{
    Computed,

    /// <summary>Janela invertida (data inicial depois da final). 400.</summary>
    Rejected,
}
