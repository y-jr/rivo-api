using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Leitura agregada de AR para composição (Fase 8, ADR-041) — o primeiro
/// dos dois contratos que o Dashboard Executivo vai precisar. Nada disto é
/// verificável no domínio: "quanto se facturou este mês" é uma invariante
/// sobre o conjunto, a mesma razão de <see cref="ISalesInvoiceStore"/>
/// separar isto da factura individual.
/// </summary>
public class ReceivablesOverviewTests
{
    private static readonly DateOnly Inicio = new(2026, 8, 1);
    private static readonly DateOnly Fim = new(2026, 8, 31);

    private static readonly Guid ClienteA = Guid.CreateVersion7();
    private static readonly Guid ClienteB = Guid.CreateVersion7();

    private static readonly DocumentSeries SerieFt = DocumentSeries.Open(DocumentType.FT, "S001");
    private static readonly DocumentSeries SerieNc = DocumentSeries.Open(DocumentType.NC, "S001");
    private static readonly DocumentSeries SerieRg = DocumentSeries.Open(DocumentType.RG, "S001");

    private static InvoicedParty Retrato(string nome = "Kianda Lda") =>
        new(nome, "5417000000", "Rua Rainha Ginga 12", "Luanda", "AO");

    private static SalesInvoice Factura(
        DateOnly emitidaEm, decimal liquido, Guid? clienteId = null, string moeda = "AOA")
    {
        // Um cliente registado exige o identificador; sem ele, o retrato só
        // pode ser o de Consumidor Final — a mesma regra que o próprio
        // agregado impõe ao emitir.
        var cliente = clienteId is null ? InvoicedParty.FinalConsumer("CONSUMIDORFINAL", "Consumidor final") : Retrato();

        return SalesInvoice.Issue(
            SerieFt.Allocate(), emitidaEm, emitidaEm, clienteId, cliente, moeda,
            [new NewInvoiceLine("Serviço", 1, liquido, "NOR", 0m)]);
    }

    private static CreditNote Nota(SalesInvoice factura, DateOnly emitidaEm, decimal liquido) =>
        CreditNote.Issue(
            SerieNc.Allocate(), factura, emitidaEm, "Devolução parcial",
            [new NewInvoiceLine("Devolução", 1, liquido, "NOR", 0m)]);

    private static Receipt Recibo(DateOnly recebidoEm, decimal valor, Guid clienteId, SalesInvoice factura) =>
        Receipt.Register(
            SerieRg.Allocate(), recebidoEm, clienteId, Retrato(), "AOA", PaymentMethod.MB,
            [new NewSettlement(factura.Id, factura.Number.Formatted, valor)]);

    [Fact]
    public async Task GetNetRevenueAsync_SomaAsFacturasDoPeriodo_ExcluiForaDele()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m))
            .With(Factura(new DateOnly(2026, 8, 20), 50_000m))
            .With(Factura(new DateOnly(2026, 7, 31), 999_999m))   // antes do período
            .With(Factura(new DateOnly(2026, 9, 1), 999_999m));   // depois do período

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var receita = await overview.GetNetRevenueAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(150_000m, receita);
    }

    [Fact]
    public async Task GetNetRevenueAsync_NotaDeCreditoReduzNoPeriodoEmQueEEmitida()
    {
        var factura = Factura(new DateOnly(2026, 7, 15), 200_000m);
        var nota = Nota(factura, new DateOnly(2026, 8, 5), 30_000m);

        var store = new FakeSalesInvoiceStore().With(factura).With(nota);
        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        // A factura é de Julho — fora do período — mas a nota, emitida em
        // Agosto, ainda reduz a receita de Agosto: é quando a correcção
        // aconteceu, não quando a venda original aconteceu.
        var receita = await overview.GetNetRevenueAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(-30_000m, receita);
    }

    [Fact]
    public async Task GetNetRevenueAsync_FacturaAnulada_NaoConta()
    {
        var factura = Factura(new DateOnly(2026, 8, 10), 100_000m);
        factura.Cancel("Emitida por engano", DateTimeOffset.UtcNow);

        var store = new FakeSalesInvoiceStore().With(factura);
        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var receita = await overview.GetNetRevenueAsync(Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(0m, receita);
    }

    [Fact]
    public async Task GetNetRevenueAsync_MoedaDiferente_NaoSeMistura()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m, moeda: "AOA"))
            .With(Factura(new DateOnly(2026, 8, 10), 5_000m, moeda: "USD"));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var emAoa = await overview.GetNetRevenueAsync(Inicio, Fim, "AOA", CancellationToken.None);
        var emUsd = await overview.GetNetRevenueAsync(Inicio, Fim, "USD", CancellationToken.None);

        Assert.Equal(100_000m, emAoa);
        Assert.Equal(5_000m, emUsd);
    }

    [Fact]
    public async Task GetOutstandingReceivablesAsync_SomaOQueFaltaReceberDeTodasAsFacturas()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 1), 100_000m))
            .With(Factura(new DateOnly(2026, 8, 2), 200_000m));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var emAberto = await overview.GetOutstandingReceivablesAsync("AOA", CancellationToken.None);

        Assert.Equal(300_000m, emAberto);
    }

    [Fact]
    public async Task GetTopCustomersAsync_OrdenaPorFacturadoENomeiaPeloContratoDeCommercial()
    {
        var facturaA1 = Factura(new DateOnly(2026, 8, 5), 100_000m, ClienteA);
        var facturaA2 = Factura(new DateOnly(2026, 8, 6), 50_000m, ClienteA);
        var facturaB = Factura(new DateOnly(2026, 8, 7), 80_000m, ClienteB);

        var store = new FakeSalesInvoiceStore().With(facturaA1).With(facturaA2).With(facturaB);
        var customers = new FakeCustomerDirectory()
            .With(new CustomerReference(
                ClienteA, "Kianda Lda", "5417000000", CustomerStatus.Active,
                new BillingAddress("Rua A", "Luanda", "AO")))
            .With(new CustomerReference(
                ClienteB, "Refriango", "5417654321", CustomerStatus.Active,
                new BillingAddress("Rua B", "Luanda", "AO")));

        var overview = new ReceivablesOverview(store, customers);

        var topo = await overview.GetTopCustomersAsync(Inicio, Fim, "AOA", 10, CancellationToken.None);

        Assert.Equal(2, topo.Count);
        Assert.Equal(ClienteA, topo[0].CustomerId);
        Assert.Equal("Kianda Lda", topo[0].CustomerName);
        Assert.Equal(150_000m, topo[0].NetRevenue);
        Assert.Equal(ClienteB, topo[1].CustomerId);
        Assert.Equal(80_000m, topo[1].NetRevenue);
    }

    [Fact]
    public async Task GetTopCustomersAsync_ConsumidorFinal_FicaDeFora()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 5), 1_000_000m, clienteId: null));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var topo = await overview.GetTopCustomersAsync(Inicio, Fim, "AOA", 10, CancellationToken.None);

        Assert.Empty(topo);
    }

    [Fact]
    public async Task GetTopCustomersAsync_RespeitaOLimite()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 5), 300_000m, ClienteA))
            .With(Factura(new DateOnly(2026, 8, 6), 200_000m, ClienteB));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var topo = await overview.GetTopCustomersAsync(Inicio, Fim, "AOA", 1, CancellationToken.None);

        Assert.Single(topo);
        Assert.Equal(ClienteA, topo[0].CustomerId);
    }

    [Fact]
    public async Task GetCustomerNetRevenueAsync_IsolaDoClienteCerto_NuncaSomaOutro()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m, ClienteA))
            .With(Factura(new DateOnly(2026, 8, 12), 999_999m, ClienteB));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var receita = await overview.GetCustomerNetRevenueAsync(ClienteA, Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(100_000m, receita);
    }

    [Fact]
    public async Task GetCustomerNetRevenueAsync_NotaDeCreditoDoClienteReduz()
    {
        var factura = Factura(new DateOnly(2026, 8, 10), 100_000m, ClienteA);
        var nota = Nota(factura, new DateOnly(2026, 8, 15), 20_000m);

        var store = new FakeSalesInvoiceStore().With(factura).With(nota);
        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var receita = await overview.GetCustomerNetRevenueAsync(ClienteA, Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(80_000m, receita);
    }

    [Fact]
    public async Task GetCustomerOutstandingAsync_IsolaDoClienteCerto_NuncaSomaOutro()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m, ClienteA))
            .With(Factura(new DateOnly(2026, 8, 10), 999_999m, ClienteB));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var emAberto = await overview.GetCustomerOutstandingAsync(ClienteA, "AOA", CancellationToken.None);

        Assert.Equal(100_000m, emAberto);
    }

    [Fact]
    public async Task ListCustomerInvoicesAsync_SoDevolveAsDoProprioCliente()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m, ClienteA))
            .With(Factura(new DateOnly(2026, 8, 11), 200_000m, ClienteB));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var facturas = await overview.ListCustomerInvoicesAsync(ClienteA, CancellationToken.None);

        Assert.Single(facturas);
    }

    [Fact]
    public async Task GetCustomerStatementAsync_SaldoDeAberturaEAsFacturasAnterioresAoPeriodo()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 7, 1), 100_000m, ClienteA));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var extracto = await overview.GetCustomerStatementAsync(ClienteA, Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(100_000m, extracto.OpeningBalance);
        Assert.Empty(extracto.Lines);
        Assert.Equal(100_000m, extracto.ClosingBalance);
    }

    [Fact]
    public async Task GetCustomerStatementAsync_MovimentosDoPeriodoComSaldoCorrido()
    {
        var factura1 = Factura(new DateOnly(2026, 8, 5), 100_000m, ClienteA);
        var nota = Nota(factura1, new DateOnly(2026, 8, 10), 20_000m);
        var recibo = Recibo(new DateOnly(2026, 8, 15), 50_000m, ClienteA, factura1);

        var store = new FakeSalesInvoiceStore().With(factura1).With(nota).With(recibo);
        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var extracto = await overview.GetCustomerStatementAsync(ClienteA, Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Equal(0m, extracto.OpeningBalance);
        Assert.Equal(3, extracto.Lines.Count);

        Assert.Equal("Factura", extracto.Lines[0].DocumentType);
        Assert.Equal("Debit", extracto.Lines[0].Direction);
        Assert.Equal(100_000m, extracto.Lines[0].BalanceAfter);

        Assert.Equal("NotaCredito", extracto.Lines[1].DocumentType);
        Assert.Equal("Credit", extracto.Lines[1].Direction);
        Assert.Equal(80_000m, extracto.Lines[1].BalanceAfter);

        Assert.Equal("Recibo", extracto.Lines[2].DocumentType);
        Assert.Equal("Credit", extracto.Lines[2].Direction);
        Assert.Equal(30_000m, extracto.Lines[2].BalanceAfter);

        Assert.Equal(30_000m, extracto.ClosingBalance);
    }

    [Fact]
    public async Task GetCustomerStatementAsync_IsolaDoClienteCerto_NuncaMisturaOutro()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 5), 100_000m, ClienteA))
            .With(Factura(new DateOnly(2026, 8, 6), 999_999m, ClienteB));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var extracto = await overview.GetCustomerStatementAsync(ClienteA, Inicio, Fim, "AOA", CancellationToken.None);

        Assert.Single(extracto.Lines);
        Assert.Equal(100_000m, extracto.ClosingBalance);
    }

    [Fact]
    public async Task GetMonthlyNetRevenueAsync_UmPontoPorMes_ComOsSemMovimentoAZero()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 10), 100_000m))
            .With(Factura(new DateOnly(2026, 10, 5), 30_000m));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var serie = await overview.GetMonthlyNetRevenueAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31), "AOA", CancellationToken.None);

        Assert.Equal(3, serie.Count);
        Assert.Equal((2026, 8, 100_000m), (serie[0].Year, serie[0].Month, serie[0].Amount));
        Assert.Equal((2026, 9, 0m), (serie[1].Year, serie[1].Month, serie[1].Amount));
        Assert.Equal((2026, 10, 30_000m), (serie[2].Year, serie[2].Month, serie[2].Amount));
    }

    [Fact]
    public async Task GetMonthlyNetRevenueAsync_NotaDeCreditoReduzNoMesEmQueEEmitida()
    {
        var factura = Factura(new DateOnly(2026, 8, 5), 100_000m);
        var nota = Nota(factura, new DateOnly(2026, 9, 10), 20_000m);

        var store = new FakeSalesInvoiceStore().With(factura).With(nota);
        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var serie = await overview.GetMonthlyNetRevenueAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30), "AOA", CancellationToken.None);

        Assert.Equal(100_000m, serie[0].Amount);
        Assert.Equal(-20_000m, serie[1].Amount);
    }

    [Fact]
    public async Task GetMonthlyNetRevenueAsync_JanelaDentroDoMesmoMes_UmSoPonto()
    {
        var store = new FakeSalesInvoiceStore()
            .With(Factura(new DateOnly(2026, 8, 5), 100_000m))
            .With(Factura(new DateOnly(2026, 8, 20), 999_999m));

        var overview = new ReceivablesOverview(store, new FakeCustomerDirectory());

        var serie = await overview.GetMonthlyNetRevenueAsync(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10), "AOA", CancellationToken.None);

        Assert.Single(serie);
        Assert.Equal(100_000m, serie[0].Amount);
    }
}
