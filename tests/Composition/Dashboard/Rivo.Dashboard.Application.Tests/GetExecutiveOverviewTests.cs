using Rivo.Finance.Contracts;

namespace Rivo.Dashboard.Application.Tests;

public class GetExecutiveOverviewTests
{
    private static readonly DateOnly Inicio = new(2026, 8, 1);
    private static readonly DateOnly Fim = new(2026, 8, 31);

    [Fact]
    public async Task ExecuteAsync_ComputaOsCincoNumeros()
    {
        var receivables = new FakeReceivablesOverview { NetRevenue = 500_000m, OutstandingReceivables = 200_000m };
        var payables = new FakePayablesOverview { NetExpenses = 300_000m, OutstandingPayables = 120_000m };
        var useCase = new GetExecutiveOverview(receivables, payables);

        var result = await useCase.ExecuteAsync(Inicio, Fim, "AOA", 5, CancellationToken.None);

        Assert.Equal(ExecutiveOverviewOutcome.Computed, result.Outcome);
        var vista = result.Overview!;
        Assert.Equal(500_000m, vista.Revenue);
        Assert.Equal(300_000m, vista.Expenses);
        Assert.Equal(200_000m, vista.Profit);
        Assert.Equal(200_000m, vista.Receivables);
        Assert.Equal(120_000m, vista.Payables);
        Assert.Equal("AOA", vista.Currency);
        Assert.Equal(Inicio, vista.From);
        Assert.Equal(Fim, vista.To);
    }

    /// <summary>
    /// Despesa acima da receita é um resultado válido — o dashboard mostra
    /// o número negativo, não o esconde nem recusa.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DespesaMaiorQueReceita_LucroNegativo()
    {
        var receivables = new FakeReceivablesOverview { NetRevenue = 100_000m };
        var payables = new FakePayablesOverview { NetExpenses = 150_000m };
        var useCase = new GetExecutiveOverview(receivables, payables);

        var result = await useCase.ExecuteAsync(Inicio, Fim, "AOA", 5, CancellationToken.None);

        Assert.Equal(-50_000m, result.Overview!.Profit);
    }

    [Fact]
    public async Task ExecuteAsync_JanelaInvertida_Recusa()
    {
        var useCase = new GetExecutiveOverview(new FakeReceivablesOverview(), new FakePayablesOverview());

        var result = await useCase.ExecuteAsync(Fim, Inicio, "AOA", 5, CancellationToken.None);

        Assert.Equal(ExecutiveOverviewOutcome.Rejected, result.Outcome);
        Assert.Null(result.Overview);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PropagaAMoedaEOPeriodoParaOsDoisLados()
    {
        var receivables = new FakeReceivablesOverview();
        var useCase = new GetExecutiveOverview(receivables, new FakePayablesOverview());

        await useCase.ExecuteAsync(Inicio, Fim, "USD", 5, CancellationToken.None);

        Assert.Equal((Inicio, Fim, "USD"), receivables.LastRevenueRequest);
    }

    [Fact]
    public async Task ExecuteAsync_DevolveOsTopClientesDoContrato()
    {
        var clienteId = Guid.CreateVersion7();
        var receivables = new FakeReceivablesOverview
        {
            TopCustomers = [new CustomerRevenueView(clienteId, "Kianda Lda", 300_000m)],
        };
        var useCase = new GetExecutiveOverview(receivables, new FakePayablesOverview());

        var result = await useCase.ExecuteAsync(Inicio, Fim, "AOA", 5, CancellationToken.None);

        var topo = Assert.Single(result.Overview!.TopCustomers);
        Assert.Equal(clienteId, topo.CustomerId);
        Assert.Equal("Kianda Lda", topo.CustomerName);
    }
}
