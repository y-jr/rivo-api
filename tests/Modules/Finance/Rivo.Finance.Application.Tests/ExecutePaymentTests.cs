using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// O caso de uso mais crítico do sistema — é aqui que sai dinheiro.
///
/// <para>
/// <strong>Nada disto é verificável no domínio.</strong> A dupla barreira de
/// BR-5 tem uma metade em `approval` e outra na conta; BR-3 precisa de saber
/// quem decidiu, e quem decidiu é `approval` que sabe. Até agora só a suite
/// caixa-preta cobria isto, e essa exige a stack de pé.
/// </para>
/// </summary>
public class ExecutePaymentTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static BankAccount Conta(decimal saldo = 200_000m, string moeda = "AOA")
    {
        var conta = BankAccount.Open("Conta operacional", "BAI", null, moeda);

        if (saldo > 0)
        {
            conta.Deposit(saldo, Agora.AddDays(-1), "Carregamento inicial");
        }

        return conta;
    }

    private static PurchaseInvoice Compra(string moeda = "AOA") =>
        PurchaseInvoice.Register(
            "FT 661054", null, new PayeeParty("Sonangol Distribuidora", "5401234567"),
            Hoje.AddDays(-5), Hoje.AddDays(25), moeda, 100_000m, 14_000m, "Combustível");

    private static PaymentRequest Pedido(
        PurchaseInvoice? compra = null,
        decimal montante = 114_000m,
        Guid? requerente = null) =>
        PaymentRequest.Create(
            compra ?? Compra(), montante, requerente ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(), Hoje);

    private static (ExecutePayment Caso, FakePayablesStore Store, FakeAuditTrail Trilha) Montar(
        FakePayablesStore store,
        FakePaymentApproval approval)
    {
        var trilha = new FakeAuditTrail();
        var relogio = new RelogioFixo(Agora);

        return (new ExecutePayment(store, approval, trilha, relogio), store, trilha);
    }

    // ---- BR-1: sem decisão aprovada não se paga ----

    [Fact]
    public async Task SemDecisaoAprovada_NaoSaiDinheiro()
    {
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval(PaymentApprovalStatus.Pending));

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.NotApproved, resultado.Outcome);
        Assert.Equal(200_000m, conta.Balance);
        Assert.Equal(PaymentRequestStatus.Eligible, pedido.Status);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// <strong>A ausência de decisão não é aprovação.</strong> Se `approval`
    /// não conhece o processo, não se paga — pagar por omissão é o modo de
    /// falha que BR-1 existe para impedir.
    /// </summary>
    [Fact]
    public async Task ProcessoDesconhecido_NaoSePagaPorOmissao()
    {
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval(PaymentApprovalStatus.Unknown));

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.NotApproved, resultado.Outcome);
        Assert.Equal(200_000m, conta.Balance);
    }

    /// <summary>
    /// BR-5, metade "decisão": entre aprovar e pagar podem passar dias, e o
    /// processo pode ter sido cancelado. A decisão é relida no momento, não
    /// lida de um campo do pedido.
    /// </summary>
    [Fact]
    public async Task DecisaoERevalidadaNoMomentoDaExecucao()
    {
        var conta = Conta();
        var pedido = Pedido();
        var approval = new FakePaymentApproval(PaymentApprovalStatus.Approved);
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, approval);

        await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(1, approval.StateReads);
    }

    // ---- BR-5: a outra metade, o saldo ----

    [Fact]
    public async Task SemSaldo_NaoSePaga()
    {
        var conta = Conta(1_000m);
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval());

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.InsufficientFunds, resultado.Outcome);
        Assert.Equal(PaymentRequestStatus.Eligible, pedido.Status);
        Assert.Equal(0, store.SaveCount);
    }

    // ---- BR-3: quem aprova não paga ----

    [Fact]
    public async Task QuemAprovouNaoPodeExecutar()
    {
        var aprovador = Guid.CreateVersion7();
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, trilha) = Montar(
            store, new FakePaymentApproval(PaymentApprovalStatus.Approved, [aprovador]));

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, aprovador, PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.SegregationOfDuties, resultado.Outcome);
        Assert.Equal(PaymentRequestStatus.Eligible, pedido.Status);
        Assert.Equal(0, store.SaveCount);

        // Uma tentativa de contornar BR-3 é evento de segurança: fica na
        // trilha com acção própria, para que uma sequência delas seja
        // detectável.
        Assert.Contains(
            trilha.Records,
            r => r.Action == FinanceAuditActions.PaymentSegregationRefused);
    }

    [Fact]
    public async Task QuemNaoDecidiu_PodeExecutar()
    {
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(
            store, new FakePaymentApproval(PaymentApprovalStatus.Approved, [Guid.CreateVersion7()]));

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, "TRF-99",
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.Executed, resultado.Outcome);
        Assert.Equal(86_000m, conta.Balance);
        Assert.Equal(86_000m, resultado.RemainingBalance);
        Assert.Equal(PaymentRequestStatus.Executed, pedido.Status);
    }

    // ---- ordem das verificações ----

    /// <summary>
    /// <strong>O defeito de 2026-08-25, agora com teste.</strong> Sacava-se da
    /// conta antes de olhar para o estado do pedido, e a segunda execução
    /// reportava falta de saldo quando a razão era "já foi pago" — mandando
    /// procurar o problema na tesouraria em vez de no pedido.
    /// </summary>
    [Fact]
    public async Task SegundaExecucao_DizQueJaFoiPagoENaoQueFaltaSaldo()
    {
        var conta = Conta(120_000m);
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval());
        var executor = Guid.CreateVersion7();

        var primeira = await caso.ExecuteAsync(
            pedido.Id, conta.Id, executor, PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.Executed, primeira.Outcome);

        // Depois da primeira, a conta tem 6.000 — o que faria a segunda
        // falhar por saldo se a ordem estivesse errada.
        Assert.Equal(6_000m, conta.Balance);

        var segunda = await caso.ExecuteAsync(
            pedido.Id, conta.Id, executor, PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.NotExecutable, segunda.Outcome);
        Assert.Contains("já foi executado", segunda.Error);
        Assert.Equal(6_000m, conta.Balance);
    }

    [Fact]
    public async Task PedidoCancelado_NaoSeExecuta()
    {
        var conta = Conta();
        var pedido = Pedido();
        pedido.Cancel("Factura em duplicado", Agora);

        var store = new FakePayablesStore().With(conta).With(pedido);
        var (caso, _, _) = Montar(store, new FakePaymentApproval());

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.NotExecutable, resultado.Outcome);
        Assert.Equal(200_000m, conta.Balance);
    }

    // ---- moeda ----

    /// <summary>
    /// Converter no acto esconderia o câmbio aplicado, e o câmbio é uma decisão
    /// que ninguém tomou aqui.
    /// </summary>
    [Fact]
    public async Task PagarEmMoedaDiferenteDaConta_ERecusado()
    {
        var conta = Conta(200_000m, "USD");
        var pedido = Pedido(Compra("AOA"));
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval());

        var resultado = await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.Rejected, resultado.Outcome);
        Assert.Equal(200_000m, conta.Balance);
    }

    /// <summary>
    /// A moeda verifica-se <strong>antes</strong> de ler a decisão: é defeito do
    /// pedido, não estado do processo, e não vale gastar a consulta.
    /// </summary>
    [Fact]
    public async Task MoedaIncompativel_NemChegaAConsultarADecisao()
    {
        var conta = Conta(200_000m, "USD");
        var pedido = Pedido(Compra("AOA"));
        var approval = new FakePaymentApproval();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, approval);

        await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(0, approval.StateReads);
    }

    // ---- não encontrados ----

    [Fact]
    public async Task PedidoInexistente_NaoEConfundidoComContaInexistente()
    {
        var conta = Conta();
        var store = new FakePayablesStore().With(conta);
        var (caso, _, _) = Montar(store, new FakePaymentApproval());

        var semPedido = await caso.ExecuteAsync(
            Guid.CreateVersion7(), conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.RequestNotFound, semPedido.Outcome);

        var pedido = Pedido();
        store.With(pedido);

        var semConta = await caso.ExecuteAsync(
            pedido.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(ExecutePaymentOutcome.AccountNotFound, semConta.Outcome);
    }

    // ---- extracto ----

    /// <summary>
    /// O pagamento tem de deixar linha no extracto, com a origem apontada — é
    /// por aí que a reconciliação volta do movimento ao documento.
    /// </summary>
    [Fact]
    public async Task PagamentoExecutado_DeixaMovimentoComOrigem()
    {
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(store, new FakePaymentApproval());

        await caso.ExecuteAsync(
            pedido.Id, conta.Id, Guid.CreateVersion7(), PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        var movimento = conta.Movements.Last();

        Assert.Equal(BankMovementDirection.Debit, movimento.Direction);
        Assert.Equal(114_000m, movimento.Amount);
        Assert.Equal(86_000m, movimento.BalanceAfter);
        Assert.Equal(BankMovementSources.PaymentRequest, movimento.SourceType);
        Assert.Equal(pedido.Id, movimento.SourceId);
    }

    /// <summary>
    /// Uma recusa por BR-3 não pode gravar nada.
    ///
    /// <para>
    /// <strong>O que se verifica é a gravação, não o objecto em memória.</strong>
    /// O saque acontece antes de <c>MarkExecuted</c> falhar, por isso a
    /// instância em memória fica com saldo abatido e com o movimento — e é
    /// correcto que fique: o que impede o efeito é nunca se chamar
    /// <c>SaveChanges</c>, e na aplicação real o contexto é descartado com o
    /// pedido HTTP.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PagamentoRecusadoPorBR3_NadaEGravado()
    {
        var aprovador = Guid.CreateVersion7();
        var conta = Conta();
        var pedido = Pedido();
        var store = new FakePayablesStore().With(conta).With(pedido);

        var (caso, _, _) = Montar(
            store, new FakePaymentApproval(PaymentApprovalStatus.Approved, [aprovador]));

        await caso.ExecuteAsync(
            pedido.Id, conta.Id, aprovador, PaymentMethod.TB, null,
            Contexto, CancellationToken.None);

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(PaymentRequestStatus.Eligible, pedido.Status);
    }
}
