using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Leitura agregada de AP para composição (Fase 8, ADR-041) — separada de
/// <see cref="ReceivablesOverviewTests"/> pela mesma razão que
/// <see cref="IPayablesStore"/> é separada de <see cref="ISalesInvoiceStore"/>
/// no módulo.
/// </summary>
public class PayablesOverviewTests
{
    private static readonly DateOnly Inicio = new(2026, 8, 1);
    private static readonly DateOnly Fim = new(2026, 8, 31);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static PayeeParty Fornecedor() => new("Sonangol Distribuidora", "5401234567");

    private static PurchaseInvoice Factura(
        string numero, DateOnly emitidaEm, decimal liquido, decimal imposto = 0m, string moeda = "AOA") =>
        PurchaseInvoice.Register(
            numero, null, null, Fornecedor(), emitidaEm, emitidaEm.AddDays(30), moeda,
            liquido, imposto, "Despesa de teste");

    private static PaymentRequest Pedido(PurchaseInvoice factura, decimal montante) =>
        PaymentRequest.Create(factura, montante, Guid.CreateVersion7(), Guid.CreateVersion7(), Inicio);

    [Fact]
    public async Task GetNetExpensesAsync_SomaAsFacturasDoPeriodo_ExcluiForaDele()
    {
        var store = new FakePayablesStore()
            .With(Factura("F-1", new DateOnly(2026, 8, 10), 100_000m))
            .With(Factura("F-2", new DateOnly(2026, 8, 20), 50_000m))
            .With(Factura("F-3", new DateOnly(2026, 7, 31), 999_999m));

        var overview = new PayablesOverview(store);

        var despesa = await overview.GetNetExpensesAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(150_000m, despesa);
    }

    [Fact]
    public async Task GetNetExpensesAsync_FacturaAnulada_NaoConta()
    {
        var factura = Factura("F-1", new DateOnly(2026, 8, 10), 100_000m);
        factura.Cancel("Duplicada", Agora);

        var store = new FakePayablesStore().With(factura);
        var overview = new PayablesOverview(store);

        var despesa = await overview.GetNetExpensesAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(0m, despesa);
    }

    [Fact]
    public async Task GetOutstandingPayablesAsync_PedidoAindaNaoExecutado_ContinuaEmFalta()
    {
        var factura = Factura("F-1", new DateOnly(2026, 8, 10), 100_000m, 14_000m);
        var pedido = Pedido(factura, 114_000m);

        var store = new FakePayablesStore().With(factura).With(pedido);
        var overview = new PayablesOverview(store);

        var emFalta = await overview.GetOutstandingPayablesAsync("AOA", CancellationToken.None);

        // Pedido submetido, dinheiro nenhum saiu ainda — diferente de
        // `CommittedAsync`, que já contaria isto como reservado.
        Assert.Equal(114_000m, emFalta);
    }

    [Fact]
    public async Task GetOutstandingPayablesAsync_PedidoExecutado_DeixaDeContarComoEmFalta()
    {
        var factura = Factura("F-1", new DateOnly(2026, 8, 10), 100_000m, 14_000m);
        var pedido = Pedido(factura, 114_000m);
        pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], Agora);

        var store = new FakePayablesStore().With(factura).With(pedido);
        var overview = new PayablesOverview(store);

        var emFalta = await overview.GetOutstandingPayablesAsync("AOA", CancellationToken.None);

        Assert.Equal(0m, emFalta);
    }

    [Fact]
    public async Task GetOutstandingPayablesAsync_SomaVariasFacturasNaoAnuladas()
    {
        var store = new FakePayablesStore()
            .With(Factura("F-1", new DateOnly(2026, 8, 1), 100_000m))
            .With(Factura("F-2", new DateOnly(2026, 8, 2), 200_000m));

        var overview = new PayablesOverview(store);

        var emFalta = await overview.GetOutstandingPayablesAsync("AOA", CancellationToken.None);

        Assert.Equal(300_000m, emFalta);
    }

    [Fact]
    public async Task Totais_NaoMisturamMoedas()
    {
        var store = new FakePayablesStore()
            .With(Factura("F-1", new DateOnly(2026, 8, 10), 100_000m, moeda: "AOA"))
            .With(Factura("F-2", new DateOnly(2026, 8, 10), 5_000m, moeda: "USD"));

        var overview = new PayablesOverview(store);

        var emAoa = await overview.GetNetExpensesAsync(Inicio, Fim, "AOA", CancellationToken.None);
        var emUsd = await overview.GetNetExpensesAsync(Inicio, Fim, "USD", CancellationToken.None);

        Assert.Equal(100_000m, emAoa);
        Assert.Equal(5_000m, emUsd);
    }
}
