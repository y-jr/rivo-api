using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Contas a Pagar e Tesouraria, na camada que orquestra — pedidos de pagamento
/// e o extracto de conta.
/// </summary>
public class PayablesTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static PurchaseInvoice Compra(decimal liquido = 100_000m, decimal imposto = 14_000m) =>
        PurchaseInvoice.Register(
            "FT 661054", null, new PayeeParty("Sonangol Distribuidora", "5401234567"),
            Hoje.AddDays(-5), Hoje.AddDays(25), "AOA", liquido, imposto, "Combustível");

    private static CreatePaymentRequest Pedir(
        FakePayablesStore store,
        FakePaymentApproval approval,
        FakePlanningStore? planning = null) =>
        new(store, planning ?? new FakePlanningStore(), approval, new FakeAuditTrail());

    // ---- BR-1 na criação: sem governança não há pedido ----

    /// <summary>
    /// Um pedido que nunca pudesse ser aprovado seria dívida a fingir que está
    /// a caminho. Sem motor de governança não se cria — e o resultado é próprio,
    /// porque é capacidade que falta, não pedido mal feito.
    /// </summary>
    [Fact]
    public async Task SemMotorDeGovernanca_NaoSeCriaPedido()
    {
        var compra = Compra();
        var store = new FakePayablesStore().With(compra);

        var resultado = await Pedir(store, new FakePaymentApproval(available: false)).ExecuteAsync(
            compra.Id, 114_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        Assert.Equal(CreatePaymentRequestOutcome.ApprovalUnavailable, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// Submissão recusada — sem política aplicável, ou sem aprovadores. O
    /// pedido não fica criado à espera de nada.
    /// </summary>
    [Fact]
    public async Task SubmissaoRecusada_NaoDeixaPedidoOrfao()
    {
        var compra = Compra();
        var store = new FakePayablesStore().With(compra);

        var approval = new FakePaymentApproval(
            submission: PaymentApprovalSubmissionResult.Failed("Sem política aplicável."));

        var resultado = await Pedir(store, approval).ExecuteAsync(
            compra.Id, 114_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        Assert.Equal(CreatePaymentRequestOutcome.ApprovalRefused, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// <strong>A invariante sobre o conjunto.</strong> Três pedidos de metade
    /// cada passariam um a um — cada um cabe no total da factura. Juntos pagam
    /// uma vez e meia, e é o agregado que não os vê uns aos outros.
    /// </summary>
    [Fact]
    public async Task PedidosSobreAMesmaFacturaAcumulam()
    {
        var compra = Compra();
        var store = new FakePayablesStore().With(compra);
        var caso = Pedir(store, new FakePaymentApproval());

        var primeiro = await caso.ExecuteAsync(
            compra.Id, 60_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        var segundo = await caso.ExecuteAsync(
            compra.Id, 54_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        var terceiro = await caso.ExecuteAsync(
            compra.Id, 1m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        Assert.Equal(CreatePaymentRequestOutcome.Created, primeiro.Outcome);
        Assert.Equal(CreatePaymentRequestOutcome.Created, segundo.Outcome);
        Assert.Equal(CreatePaymentRequestOutcome.ExceedsInvoice, terceiro.Outcome);
    }

    /// <summary>
    /// Um pedido cancelado liberta o que tinha comprometido — senão um engano
    /// bloquearia a factura para sempre.
    /// </summary>
    [Fact]
    public async Task PedidoCancelado_LibertaOComprometido()
    {
        var compra = Compra();
        var store = new FakePayablesStore().With(compra);
        var caso = Pedir(store, new FakePaymentApproval());

        var primeiro = await caso.ExecuteAsync(
            compra.Id, 114_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        var pedido = await store.FindPaymentRequestAsync(
            primeiro.PaymentRequestId!.Value, CancellationToken.None);

        pedido!.Cancel("Factura em duplicado", Agora);

        var segundo = await caso.ExecuteAsync(
            compra.Id, 114_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        Assert.Equal(CreatePaymentRequestOutcome.Created, segundo.Outcome);
    }

    [Fact]
    public async Task FacturaDeCompraInexistente_ERecusada()
    {
        var store = new FakePayablesStore();

        var resultado = await Pedir(store, new FakePaymentApproval()).ExecuteAsync(
            Guid.CreateVersion7(), 1_000m, Guid.CreateVersion7(), Hoje, costCentreId: null, null,
            Contexto, CancellationToken.None);

        Assert.Equal(CreatePaymentRequestOutcome.InvoiceNotFound, resultado.Outcome);
    }

    // ---- extracto ----

    private static BankAccount ContaComHistorico()
    {
        var conta = BankAccount.Open("Conta operacional", "BAI", null, "AOA");

        conta.Deposit(200_000m, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "Carregamento");
        conta.Withdraw(114_000m, new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero), "Pagamento a fornecedor");
        conta.Deposit(50_000m, new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), "Carregamento");

        return conta;
    }

    private static GetAccountStatement Extracto(FakePayablesStore store) => new(store);

    [Fact]
    public async Task ExtractoDeContaInexistente_ENulo()
    {
        var resultado = await Extracto(new FakePayablesStore())
            .ExecuteAsync(Guid.CreateVersion7(), null, null, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ExtractoCompleto_AbreAZeroEFechaNoSaldo()
    {
        var conta = ContaComHistorico();
        var store = new FakePayablesStore().With(conta);

        var extracto = await Extracto(store)
            .ExecuteAsync(conta.Id, null, null, CancellationToken.None);

        Assert.NotNull(extracto);
        Assert.Equal(0m, extracto.OpeningBalance);
        Assert.Equal(250_000m, extracto.TotalCredits);
        Assert.Equal(114_000m, extracto.TotalDebits);
        Assert.Equal(136_000m, extracto.ClosingBalance);
        Assert.Equal(3, extracto.Movements.Count);
    }

    /// <summary>
    /// <strong>A pergunta que o extracto existe para responder:</strong> o que o
    /// Rivo diz bate com o que a conta diz? Expor os dois lado a lado é o que
    /// faz uma divergência aparecer em vez de ser absorvida.
    /// </summary>
    [Fact]
    public async Task ExtractoAteHoje_ReconciliaComOSaldoDaConta()
    {
        var conta = ContaComHistorico();
        var store = new FakePayablesStore().With(conta);

        var extracto = await Extracto(store)
            .ExecuteAsync(conta.Id, null, null, CancellationToken.None);

        Assert.True(extracto!.Reconciles);
        Assert.Equal(conta.Balance, extracto.ClosingBalance);
    }

    /// <summary>
    /// Num extracto de Março, o fecho <strong>não deve</strong> bater com o
    /// saldo de hoje. Dizer que não reconcilia seria mentir ao contrário — por
    /// isso a pergunta não se aplica.
    /// </summary>
    [Fact]
    public async Task ExtractoDeJanelaFechada_NaoAfirmaReconciliacao()
    {
        var conta = ContaComHistorico();
        var store = new FakePayablesStore().With(conta);

        var extracto = await Extracto(store).ExecuteAsync(
            conta.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
            CancellationToken.None);

        Assert.Null(extracto!.Reconciles);
        Assert.Equal(86_000m, extracto.ClosingBalance);
        Assert.NotEqual(conta.Balance, extracto.ClosingBalance);
    }

    /// <summary>
    /// O saldo de abertura vem do movimento anterior à janela, não de uma soma
    /// dos que estão dentro dela.
    /// </summary>
    [Fact]
    public async Task JanelaPosterior_AbreComOSaldoQueVinhaDeTras()
    {
        var conta = ContaComHistorico();
        var store = new FakePayablesStore().With(conta);

        var extracto = await Extracto(store).ExecuteAsync(
            conta.Id, new DateOnly(2026, 7, 1), null, CancellationToken.None);

        Assert.Equal(86_000m, extracto!.OpeningBalance);
        Assert.Equal(50_000m, extracto.TotalCredits);
        Assert.Equal(0m, extracto.TotalDebits);
        Assert.Equal(136_000m, extracto.ClosingBalance);
        Assert.Single(extracto.Movements);
    }

    [Fact]
    public async Task ContaSemMovimentos_DaExtractoVazioQueReconcilia()
    {
        var conta = BankAccount.Open("Conta nova", "BFA", null, "AOA");
        var store = new FakePayablesStore().With(conta);

        var extracto = await Extracto(store)
            .ExecuteAsync(conta.Id, null, null, CancellationToken.None);

        Assert.Empty(extracto!.Movements);
        Assert.Equal(0m, extracto.ClosingBalance);
        Assert.True(extracto.Reconciles);
    }

    /// <summary>
    /// O carregamento de conta não é o recebimento de uma factura. Ligar os dois
    /// é Contabilidade &amp; Fecho, que não existe — misturá-los aqui faria o
    /// saldo bater por acidente e depois deixar de bater.
    /// </summary>
    [Fact]
    public async Task Carregamento_EntraNoExtractoSemOrigemDocumental()
    {
        var conta = BankAccount.Open("Conta nova", "BFA", null, "AOA");
        var store = new FakePayablesStore().With(conta);
        var caso = new DepositToAccount(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await caso.ExecuteAsync(
            conta.Id, 75_000m, "Transferência do sócio", Contexto, CancellationToken.None);

        Assert.Equal(AccountMovementOutcome.Done, resultado);

        var movimento = Assert.Single(conta.Movements);

        Assert.Equal(Agora, movimento.OccurredAt);
        Assert.Equal("Transferência do sócio", movimento.Description);
        Assert.Null(movimento.SourceType);
    }

    [Fact]
    public async Task CarregamentoRecusado_NaoDeixaMovimento()
    {
        var conta = BankAccount.Open("Conta nova", "BFA", null, "AOA");
        var store = new FakePayablesStore().With(conta);
        var caso = new DepositToAccount(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await caso.ExecuteAsync(
            conta.Id, -5m, null, Contexto, CancellationToken.None);

        Assert.Equal(AccountMovementOutcome.Rejected, resultado);
        Assert.Empty(conta.Movements);
        Assert.Equal(0, store.SaveCount);
    }
}
