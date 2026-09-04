using Rivo.Finance.Contracts;
using Rivo.Fleet.Contracts;
using Rivo.Inventory.Contracts;

namespace Rivo.Analytics.Application;

/// <summary>
/// Analytics &amp; IA (módulo 10, Fase 8) — dashboards analíticos mais
/// profundos que o Dashboard Executivo: tendência mensal de receita/despesa
/// (não só o total do período), mais actividade de Frota e valorização de
/// Inventário. Âmbito fixado por decisão explícita do utilizador
/// (2026-09-04): sem HR, sem alertas, sem previsões de IA (ADR-047).
///
/// <para>
/// <strong>Camada de composição, não módulo.</strong> Não possui dados
/// próprios: lê `finance`, `fleet` e `inventory` pelos seus contratos
/// publicados, mesmo padrão que `Rivo.Dashboard.Application` já usa.
/// </para>
///
/// <para>
/// <strong>Frota não tem "custos de manutenção" para mostrar.</strong> O
/// utilizador pediu essa métrica, mas o domínio de `fleet` não a suporta —
/// `MaintenanceRecord` não tem campo de custo e `FleetExpenseCategory` não
/// tem categoria `Maintenance` (só Combustível/Portagem/Estacionamento).
/// <see cref="IFleetActivityOverview"/> expõe só o que o domínio tem hoje
/// (despesas do período, distância percorrida) — o vazio fica registado em
/// `pending-decisions.md`, não inventado aqui.
/// </para>
/// </summary>
public sealed class GetAnalyticsOverview(
    IReceivablesOverview receivables,
    IPayablesOverview payables,
    IFleetActivityOverview fleet,
    IInventoryValuationOverview inventory)
{
    public async Task<AnalyticsOverviewResult> ExecuteAsync(
        DateOnly from,
        DateOnly to,
        string currency,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return AnalyticsOverviewResult.Rejected("A data inicial não pode ser posterior à data final.");
        }

        var receitaMensal = await receivables.GetMonthlyNetRevenueAsync(from, to, currency, cancellationToken);
        var despesaMensal = await payables.GetMonthlyNetExpensesAsync(from, to, currency, cancellationToken);
        var despesaFrota = await fleet.GetPeriodExpensesAsync(from, to, cancellationToken);
        var distanciaFrota = await fleet.GetPeriodDistanceAsync(from, to, cancellationToken);
        var valorStockAgora = await inventory.GetCurrentStockValueAsync(cancellationToken);
        var valorizacaoPeriodo = await inventory.GetPeriodValuationAsync(from, to, cancellationToken);

        return AnalyticsOverviewResult.Success(new AnalyticsOverviewView(
            from,
            to,
            currency,
            receitaMensal,
            despesaMensal,
            despesaFrota,
            distanciaFrota,
            valorStockAgora,
            valorizacaoPeriodo));
    }
}

/// <param name="MonthlyRevenue">Tendência mensal de receita líquida — um ponto por mês, ver <see cref="IReceivablesOverview.GetMonthlyNetRevenueAsync"/>.</param>
/// <param name="MonthlyExpenses">Tendência mensal de despesa líquida, mesma janela.</param>
/// <param name="FleetPeriodExpenses">Despesas de frota no período (combustível, portagens, estacionamento) — não inclui manutenção, ver o comentário na classe.</param>
/// <param name="FleetPeriodDistanceKm">Distância percorrida por toda a frota no período.</param>
/// <param name="InventoryCurrentValue">Valor do stock agora — estado corrente, não uma data passada.</param>
/// <param name="InventoryPeriodValuation">Valor movimentado no período (entradas menos saídas), inventário inteiro.</param>
public sealed record AnalyticsOverviewView(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<MonthlyAmount> MonthlyRevenue,
    IReadOnlyList<MonthlyAmount> MonthlyExpenses,
    decimal FleetPeriodExpenses,
    decimal FleetPeriodDistanceKm,
    decimal InventoryCurrentValue,
    decimal InventoryPeriodValuation);

public sealed record AnalyticsOverviewResult(
    AnalyticsOverviewOutcome Outcome, AnalyticsOverviewView? Overview, string? Error)
{
    public static AnalyticsOverviewResult Success(AnalyticsOverviewView overview) =>
        new(AnalyticsOverviewOutcome.Computed, overview, null);

    public static AnalyticsOverviewResult Rejected(string error) =>
        new(AnalyticsOverviewOutcome.Rejected, null, error);
}

public enum AnalyticsOverviewOutcome
{
    Computed,

    /// <summary>Janela invertida (data inicial depois da final). 400.</summary>
    Rejected,
}
