using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// O ciclo de venda visto da camada que o orquestra.
///
/// <para>
/// As duas regras que mais custam se falharem <strong>não vivem em agregado
/// nenhum</strong>: o que falta receber de uma factura é uma invariante sobre o
/// conjunto — nem a factura vê as suas notas de crédito, nem o recibo vê os
/// outros recibos — e a taxa à data do facto gerador é orquestração entre
/// `fiscal` e `finance`.
/// </para>
/// </summary>
public class SalesCycleTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Sem regra de postagem configurada — que é o estado por omissão, e o que
    /// estes testes querem: o ciclo de venda não depende de contabilidade.
    /// </summary>
    private static PostDocument SemPostagem() => new(new FakeLedgerStore());

    private static CustomerReference Cliente(CustomerStatus estado = CustomerStatus.Active) =>
        new(ClienteId, "Refriango", "5417654321", estado,
            new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    private static IReadOnlyList<InvoiceLineInput> UmaLinha(decimal preco = 100_000m) =>
        [new InvoiceLineInput("Serviço de consultoria", 1m, preco, "NOR")];

    private static IssueSalesInvoice Emissao(
        FakeSalesInvoiceStore store,
        FakeTaxDetermination impostos,
        CustomerReference? cliente = null,
        string finalConsumerTaxId = "CONSUMIDORFINAL") =>
        new(store, new FakeCustomerDirectory(cliente), impostos, new FakeAuditTrail(),
            SemPostagem(), new RelogioFixo(Agora), Opcoes.Financeiras(finalConsumerTaxId));

    /// <summary>Uma factura já emitida, para os casos que partem dela.</summary>
    private static SalesInvoice FacturaDe(decimal bruto = 114_000m)
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");
        var liquido = bruto / 1.14m;

        return SalesInvoice.Issue(
            serie.Allocate(), Hoje, Hoje, ClienteId,
            new InvoicedParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO"),
            "AOA",
            [new NewInvoiceLine("Serviço", 1m, decimal.Round(liquido, 2), "NOR", 14m)]);
    }

    // ---- emissão ----

    [Fact]
    public async Task FacturaSemLinhas_ERecusada()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(store, new FakeTaxDetermination(), Cliente())
            .ExecuteAsync(ClienteId, "S001", Hoje, null, "AOA", [], Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ClienteInexistente_ERecusado()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(store, new FakeTaxDetermination(), cliente: null)
            .ExecuteAsync(
                Guid.CreateVersion7(), "S001", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.CustomerNotFound, resultado.Outcome);
    }

    /// <summary>
    /// O cliente foi desactivado justamente para deixar de aparecer nestes
    /// fluxos — facturá-lo é quase sempre engano.
    /// </summary>
    [Fact]
    public async Task ClienteDesactivado_NaoSeFactura()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(store, new FakeTaxDetermination(), Cliente(CustomerStatus.Inactive))
            .ExecuteAsync(
                ClienteId, "S001", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Contains("desactivado", resultado.Error);
    }

    /// <summary>
    /// ADR-011 §3: a taxa é a do <strong>facto gerador</strong>, não a de hoje.
    /// Verificável observando a data por que a determinação foi perguntada.
    /// </summary>
    [Fact]
    public async Task TaxaEDeterminadaADataDoFactoGerador()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);
        var impostos = new FakeTaxDetermination();
        var facto = new DateOnly(2026, 3, 15);

        await Emissao(store, impostos, Cliente()).ExecuteAsync(
            ClienteId, "S001", Hoje, facto, "AOA", UmaLinha(),
            Contexto, CancellationToken.None);

        Assert.Equal(facto, Assert.Single(impostos.AskedFor));
    }

    [Fact]
    public async Task SemFactoGeradorExplicito_ValeADataDoDocumento()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);
        var impostos = new FakeTaxDetermination();

        await Emissao(store, impostos, Cliente()).ExecuteAsync(
            ClienteId, "S001", Hoje, null, "AOA", UmaLinha(),
            Contexto, CancellationToken.None);

        Assert.Equal(Hoje, Assert.Single(impostos.AskedFor));
    }

    /// <summary>
    /// <strong>Um número de documento queimado não volta.</strong> Se a
    /// determinação fiscal falha, a factura não pode chegar a nascer — senão
    /// fica um salto na numeração que a AGT lê como documento em falta.
    /// </summary>
    [Fact]
    public async Task SemTaxaEmVigor_NaoSeQueimaNumeroDeSerie()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);
        var antes = store.NextSequenceOf(DocumentType.FT);

        var resultado = await Emissao(
                store,
                new FakeTaxDetermination(TaxDeterminationResult.NoRateInForce()),
                Cliente())
            .ExecuteAsync(
                ClienteId, "S001", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Equal(antes, store.NextSequenceOf(DocumentType.FT));
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// ADR-036: não se inventa código de isenção. A recusa é própria — é
    /// capacidade que falta, não pedido mal feito.
    /// </summary>
    [Fact]
    public async Task Isencao_ERecusadaComResultadoProprio()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(
                store,
                new FakeTaxDetermination(TaxDeterminationResult.ExemptionCodeUnavailable()),
                Cliente())
            .ExecuteAsync(
                ClienteId, "S001", Hoje, null, "AOA",
                [new InvoiceLineInput("Serviço isento", 1m, 1_000m, TaxCodes.Exempt)],
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.ExemptionUnavailable, resultado.Outcome);
    }

    // ---- consumidor final ----

    [Fact]
    public async Task ConsumidorFinal_DispensaCliente()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(store, new FakeTaxDetermination(), cliente: null)
            .ExecuteAsync(
                customerId: null, "S001", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.Issued, resultado.Outcome);
    }

    /// <summary>
    /// O identificador de consumidor final vem de configuração porque a
    /// convenção angolana não está verificada em fonte primária. Sem ele,
    /// recusa-se e diz-se porquê — não se inventa.
    /// </summary>
    [Fact]
    public async Task ConsumidorFinalSemIdentificadorConfigurado_ERecusado()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.FT);

        var resultado = await Emissao(
                store, new FakeTaxDetermination(), cliente: null, finalConsumerTaxId: "  ")
            .ExecuteAsync(
                customerId: null, "S001", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Contains("FinalConsumerTaxId", resultado.Error);
    }

    [Fact]
    public async Task SerieInexistente_ERecusada()
    {
        var store = new FakeSalesInvoiceStore();

        var resultado = await Emissao(store, new FakeTaxDetermination(), Cliente())
            .ExecuteAsync(
                ClienteId, "SEM-SERIE", Hoje, null, "AOA", UmaLinha(),
                Contexto, CancellationToken.None);

        Assert.Equal(IssueInvoiceOutcome.SeriesNotFound, resultado.Outcome);
    }

    // ---- recibos: a regra do saldo ----

    private static RegisterReceipt Recebimento(FakeSalesInvoiceStore store) =>
        new(store, new FakeAuditTrail(), SemPostagem(), new RelogioFixo(Agora), Opcoes.Financeiras());

    [Fact]
    public async Task ReceberMaisDoQueEstaEmAberto_ERecusado()
    {
        var factura = FacturaDe(114_000m);
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG).With(factura);

        var resultado = await Recebimento(store).ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB,
            [new SettlementInput(factura.Id, 200_000m)], null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterReceiptOutcome.ExceedsOutstanding, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// <strong>A invariante que nenhum agregado vê.</strong> Dois recebimentos
    /// parciais passam um a um; o terceiro tem de esbarrar no que os dois
    /// primeiros já liquidaram.
    /// </summary>
    [Fact]
    public async Task RecebimentosParciaisAcumulam_ETerceiroExcede()
    {
        var factura = FacturaDe(114_000m);
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG).With(factura);
        var caso = Recebimento(store);

        var primeiro = await caso.ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB,
            [new SettlementInput(factura.Id, 60_000m)], null,
            Contexto, CancellationToken.None);

        var segundo = await caso.ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB,
            [new SettlementInput(factura.Id, 54_000m)], null,
            Contexto, CancellationToken.None);

        var terceiro = await caso.ExecuteAsync(
            "S001", Hoje, PaymentMethod.NU,
            [new SettlementInput(factura.Id, 1m)], null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterReceiptOutcome.Registered, primeiro.Outcome);
        Assert.Equal(RegisterReceiptOutcome.Registered, segundo.Outcome);
        Assert.Equal(RegisterReceiptOutcome.ExceedsOutstanding, terceiro.Outcome);
    }

    [Fact]
    public async Task ReciboSemLiquidacoes_ERecusado()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG);

        var resultado = await Recebimento(store).ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB, [], null, Contexto, CancellationToken.None);

        Assert.Equal(RegisterReceiptOutcome.Rejected, resultado.Outcome);
    }

    [Fact]
    public async Task ReciboSobreFacturaInexistente_ERecusado()
    {
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG);

        var resultado = await Recebimento(store).ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB,
            [new SettlementInput(Guid.CreateVersion7(), 100m)], null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterReceiptOutcome.InvoiceNotFound, resultado.Outcome);
    }

    // ---- nota de crédito ----

    private static IssueCreditNote Credito(FakeSalesInvoiceStore store, FakeTaxDetermination impostos) =>
        new(store, impostos, new FakeAuditTrail(), SemPostagem(), new RelogioFixo(Agora), Opcoes.Financeiras());

    /// <summary>
    /// ADR-011 §3 outra vez, e aqui é onde mais custa: o imposto que se devolve
    /// é o que foi liquidado. Uma correcção emitida hoje sobre um facto de Março
    /// aplica a taxa de Março.
    /// </summary>
    [Fact]
    public async Task NotaDeCredito_HerdaOFactoGeradorDaFactura()
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");
        var facto = new DateOnly(2026, 3, 15);

        var factura = SalesInvoice.Issue(
            serie.Allocate(), new DateOnly(2026, 3, 20), facto, ClienteId,
            new InvoicedParty("Refriango", "5417654321", "Rua", "Luanda", "AO"),
            "AOA", [new NewInvoiceLine("Serviço", 1m, 100_000m, "NOR", 14m)]);

        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.NC).With(factura);
        var impostos = new FakeTaxDetermination();

        await Credito(store, impostos).ExecuteAsync(
            factura.Id, "S001", Hoje, "Devolução parcial",
            [new InvoiceLineInput("Serviço", 1m, 10_000m, "NOR")],
            Contexto, CancellationToken.None);

        // Perguntou-se pela data da factura corrigida, não por hoje.
        Assert.Equal(facto, Assert.Single(impostos.AskedFor));
        Assert.NotEqual(Hoje, impostos.AskedFor[0]);
    }

    /// <summary>
    /// Creditar mais do que está em aberto poria a factura com saldo negativo —
    /// dívida ao contrário, que não é o que uma nota de crédito significa.
    /// </summary>
    [Fact]
    public async Task CreditarMaisDoQueEstaEmAberto_ERecusado()
    {
        var factura = FacturaDe(114_000m);
        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.NC).With(factura);

        var resultado = await Credito(store, new FakeTaxDetermination()).ExecuteAsync(
            factura.Id, "S001", Hoje, "Devolução total e mais um pouco",
            [new InvoiceLineInput("Serviço", 1m, 200_000m, "NOR")],
            Contexto, CancellationToken.None);

        Assert.Equal(IssueCreditNoteOutcome.ExceedsOutstanding, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// Crédito e recebimento consomem o mesmo saldo. Creditar metade e depois
    /// receber o total tem de falhar — senão a factura fica paga a mais.
    /// </summary>
    [Fact]
    public async Task CreditoERecebimentoConsomemOMesmoSaldo()
    {
        var factura = FacturaDe(114_000m);
        var store = new FakeSalesInvoiceStore()
            .WithSeries(DocumentType.NC, DocumentType.RG)
            .With(factura);

        var credito = await Credito(store, new FakeTaxDetermination()).ExecuteAsync(
            factura.Id, "S001", Hoje, "Devolução parcial",
            [new InvoiceLineInput("Serviço", 1m, 50_000m, "NOR")],
            Contexto, CancellationToken.None);

        Assert.Equal(IssueCreditNoteOutcome.Issued, credito.Outcome);

        var recibo = await Recebimento(store).ExecuteAsync(
            "S001", Hoje, PaymentMethod.TB,
            [new SettlementInput(factura.Id, 114_000m)], null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterReceiptOutcome.ExceedsOutstanding, recibo.Outcome);
    }
}
