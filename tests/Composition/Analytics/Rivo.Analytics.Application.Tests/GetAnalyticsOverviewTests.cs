using Rivo.Finance.Contracts;

namespace Rivo.Analytics.Application.Tests;

public class GetAnalyticsOverviewTests
{
    private static readonly DateOnly Inicio = new(2026, 7, 1);
    private static readonly DateOnly Fim = new(2026, 8, 31);

    private static GetAnalyticsOverview NovoCasoDeUso(
        FakeReceivablesOverview? receivables = null,
        FakePayablesOverview? payables = null,
        FakeFleetActivityOverview? fleet = null,
        FakeInventoryValuationOverview? inventory = null) =>
        new(
            receivables ?? new FakeReceivablesOverview(),
            payables ?? new FakePayablesOverview(),
            fleet ?? new FakeFleetActivityOverview(),
            inventory ?? new FakeInventoryValuationOverview());

    [Fact]
    public async Task ExecuteAsync_ComputaAsTendenciasMensaisDeFinancaEAActividadeDeFrotaEInventario()
    {
        var receivables = new FakeReceivablesOverview
        {
            MonthlyRevenue = [new MonthlyAmount(2026, 7, 500_000m), new MonthlyAmount(2026, 8, 600_000m)],
        };
        var payables = new FakePayablesOverview
        {
            MonthlyExpenses = [new MonthlyAmount(2026, 7, 300_000m), new MonthlyAmount(2026, 8, 320_000m)],
        };
        var fleet = new FakeFleetActivityOverview { PeriodExpenses = 45_000m, PeriodDistance = 1_200m };
        var inventory = new FakeInventoryValuationOverview { CurrentStockValue = 2_000_000m, PeriodValuation = 150_000m };
        var useCase = NovoCasoDeUso(receivables, payables, fleet, inventory);

        var result = await useCase.ExecuteAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(AnalyticsOverviewOutcome.Computed, result.Outcome);
        var vista = result.Overview!;
        Assert.Equal(2, vista.MonthlyRevenue.Count);
        Assert.Equal(2, vista.MonthlyExpenses.Count);
        Assert.Equal(45_000m, vista.FleetPeriodExpenses);
        Assert.Equal(1_200m, vista.FleetPeriodDistanceKm);
        Assert.Equal(2_000_000m, vista.InventoryCurrentValue);
        Assert.Equal(150_000m, vista.InventoryPeriodValuation);
        Assert.Equal("AOA", vista.Currency);
        Assert.Equal(Inicio, vista.From);
        Assert.Equal(Fim, vista.To);
    }

    [Fact]
    public async Task ExecuteAsync_JanelaInvertida_Recusa()
    {
        var useCase = NovoCasoDeUso();

        var result = await useCase.ExecuteAsync(Fim, Inicio, "AOA", CancellationToken.None);

        Assert.Equal(AnalyticsOverviewOutcome.Rejected, result.Outcome);
        Assert.Null(result.Overview);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PropagaAMoedaEOPeriodoParaAsLeiturasDeFinanca()
    {
        var receivables = new FakeReceivablesOverview();
        var useCase = NovoCasoDeUso(receivables: receivables);

        await useCase.ExecuteAsync(Inicio, Fim, "USD", CancellationToken.None);

        Assert.Equal((Inicio, Fim, "USD"), receivables.LastMonthlyRequest);
    }
}
