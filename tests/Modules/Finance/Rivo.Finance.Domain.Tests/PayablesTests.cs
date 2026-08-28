using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Contas a Pagar e Tesouraria. É onde BR-1, BR-3, BR-5 e BR-17 se encontram.
/// </summary>
public class PayablesTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Instante = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static PayeeParty Fornecedor() => new("Sonangol Distribuidora", "5401234567");

    private static PurchaseInvoice Compra(decimal liquido = 100_000m, decimal imposto = 14_000m) =>
        PurchaseInvoice.Register(
            "FT 2026/8891", null, Fornecedor(), Hoje.AddDays(-5), Hoje.AddDays(25),
            "AOA", liquido, imposto, "Combustivel");

    private static BankAccount Conta(decimal saldo = 500_000m)
    {
        var conta = BankAccount.Open("Conta operacional", "BAI", "AO06004000011234567890123", "AOA");

        if (saldo > 0)
        {
            conta.Deposit(saldo, Instante, "Carregamento inicial");
        }

        return conta;
    }

    private static PaymentRequest Pedido(
        PurchaseInvoice? compra = null,
        decimal amount = 114_000m,
        Guid? requerente = null) =>
        PaymentRequest.Create(
            compra ?? Compra(), amount, requerente ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(), Hoje);

    // ---- conta bancária ----

    [Fact]
    public void ContaAberta_NasceComSaldoZeroEActiva()
    {
        var conta = BankAccount.Open("Operacional", "BAI", null, "AOA");

        Assert.Equal(0m, conta.Balance);
        Assert.True(conta.IsActive);
    }

    [Fact]
    public void IbanENormalizadoSemEspacos()
    {
        var conta = BankAccount.Open("X", "BAI", "AO06 0040 0001 1234", "aoa");

        Assert.Equal("AO06004000011234", conta.Iban);
        Assert.Equal("AOA", conta.Currency);
    }

    /// <summary>
    /// A metade "saldo" da dupla barreira de BR-5. A outra depende de
    /// `approval` e vive na camada Application.
    /// </summary>
    [Fact]
    public void SacarMaisDoQueOSaldo_ERecusado()
    {
        var conta = Conta(1_000m);

        var erro = Assert.Throws<InsufficientFundsException>(() => conta.Withdraw(1_000.01m, Instante, "Tentativa"));

        // Sem asserir o número formatado: `N2` segue a cultura corrente, e o
        // teste passaria a depender de onde corre.
        Assert.Contains("Conta operacional", erro.Message);

        // O que importa: a recusa não deixou o saldo a meio.
        Assert.Equal(1_000m, conta.Balance);
    }

    [Fact]
    public void SacarExactamenteOSaldo_EAceite()
    {
        var conta = Conta(1_000m);
        conta.Withdraw(1_000m, Instante, "Pagamento");

        Assert.Equal(0m, conta.Balance);
    }

    [Fact]
    public void ContaFechada_NaoMovimenta()
    {
        // So se fecha com saldo zero (ver ContaComSaldo_NaoFecha) — por isso o
        // cenario aqui e uma conta sem dinheiro dentro.
        var conta = Conta(saldo: 0);
        conta.Close();

        Assert.Throws<InvalidOperationException>(() => conta.Withdraw(1m, Instante, "Pagamento"));
        Assert.Throws<InvalidOperationException>(() => conta.Deposit(1m, Instante, null));
    }

    [Fact]
    public void ContaComSaldo_NaoFecha()
    {
        // Fechar uma conta com dinheiro dentro esconderia esse dinheiro atras
        // de uma conta que diz nao estar em uso.
        var conta = Conta();

        Assert.Throws<InvalidOperationException>(() => conta.Close());
        Assert.True(conta.IsActive);
    }

    [Fact]
    public void ContaFechada_ComSaldoZero_Reabre()
    {
        var conta = Conta(saldo: 0);
        conta.Close();

        conta.Reopen();

        Assert.True(conta.IsActive);
        conta.Deposit(1m, Instante, null);
        Assert.Equal(1m, conta.Balance);
    }

    [Fact]
    public void LevantarSaldoAteZero_DepoisFecha()
    {
        // O caminho real: esvaziar a conta e so depois fecha-la.
        var conta = Conta();
        conta.Withdraw(conta.Balance, Instante, "Transferencia de encerramento");

        conta.Close();

        Assert.False(conta.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MovimentoNaoPositivo_ERecusado(decimal valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Conta().Withdraw(valor, Instante, "Pagamento"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Conta().Deposit(valor, Instante, null));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDaConta()
    {
        var conta = Conta();
        conta.Withdraw(100m, Instante, "Pagamento");

        Assert.Equal(0, conta.Version);
    }

    // ---- factura de compra ----

    /// <summary>
    /// O número vem do fornecedor. Numerá-la em série do Rivo faria o sistema
    /// numerar documentos que não emitiu.
    /// </summary>
    [Fact]
    public void FacturaDeCompraGuardaONumeroDoFornecedor()
    {
        Assert.Equal("FT 2026/8891", Compra().SupplierInvoiceNumber);
    }

    [Fact]
    public void FacturaDeCompraSemNumeroDoFornecedor_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoice.Register("  ", null, Fornecedor(), Hoje, Hoje, "AOA", 100m, 0m));
    }

    [Fact]
    public void TotalEASomaDeLiquidoEImposto()
    {
        var compra = Compra(100_000m, 14_000m);

        Assert.Equal(114_000m, compra.GrossTotal);
    }

    [Fact]
    public void VencimentoAnteriorAEmissao_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            PurchaseInvoice.Register(
                "FT 1", null, Fornecedor(), Hoje, Hoje.AddDays(-1), "AOA", 100m, 0m));
    }

    [Fact]
    public void FornecedorSemNif_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => new PayeeParty("Sonangol", ""));
    }

    // ---- pedido de pagamento ----

    /// <summary>
    /// **BR-1.** Um pedido nasce com processo de aprovação ou não nasce: criá-lo
    /// primeiro e submetê-lo depois deixaria uma janela em que existe um pedido
    /// pagável sem decisão.
    /// </summary>
    [Fact]
    public void PedidoSemProcessoDeAprovacao_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentRequest.Create(Compra(), 1_000m, Guid.CreateVersion7(), Guid.Empty, Hoje));
    }

    [Fact]
    public void PedidoNasceElegivel()
    {
        Assert.Equal(PaymentRequestStatus.Eligible, Pedido().Status);
    }

    /// <summary>
    /// O anti-padrão do protótipo: `payment_requests` tinha o workflow na
    /// própria tabela. Aqui os estados são dois — não há "pendente de
    /// aprovação", porque esse é estado do processo, não do pedido.
    /// </summary>
    [Fact]
    public void PedidoNaoTemEstadoDeAprovacao()
    {
        var estados = Enum.GetNames<PaymentRequestStatus>();

        Assert.Equal(["Eligible", "Executed", "Cancelled"], estados);
        Assert.DoesNotContain(estados, e => e.Contains("Approv", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(estados, e => e.Contains("Pending", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PedidoMaiorQueAFactura_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pedido(Compra(), 114_000.01m));
    }

    [Fact]
    public void PagamentoParcial_EAceite()
    {
        Assert.Equal(50_000m, Pedido(Compra(), 50_000m).Amount);
    }

    [Fact]
    public void PedidoSobreFacturaAnulada_ERecusado()
    {
        var compra = Compra();
        compra.Cancel("Duplicada", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => Pedido(compra));
    }

    // ---- execução ----

    [Fact]
    public void ExecutarMarcaEGuardaQuemPagou()
    {
        var pedido = Pedido();
        var quemPaga = Guid.CreateVersion7();
        var conta = Guid.CreateVersion7();
        var quando = DateTimeOffset.UtcNow;

        pedido.MarkExecuted(conta, quemPaga, PaymentMethod.TB, [], quando, "REF-001");

        Assert.Equal(PaymentRequestStatus.Executed, pedido.Status);
        Assert.Equal(quemPaga, pedido.ExecutedByEmployeeId);
        Assert.Equal(conta, pedido.ExecutedFromAccountId);
        Assert.Equal(PaymentMethod.TB, pedido.ExecutedMethod);
        Assert.Equal("REF-001", pedido.ExecutionReference);
    }

    /// <summary>**BR-3.** Quem aprova não paga.</summary>
    [Fact]
    public void QuemDecidiu_NaoPodeExecutar()
    {
        var pedido = Pedido();
        var decisor = Guid.CreateVersion7();

        var erro = Assert.Throws<SegregationOfDutiesException>(() =>
            pedido.MarkExecuted(Guid.CreateVersion7(), decisor, PaymentMethod.TB, [decisor], DateTimeOffset.UtcNow));

        Assert.Contains("BR-3", erro.Message);
        Assert.Equal(PaymentRequestStatus.Eligible, pedido.Status);
    }

    [Fact]
    public void QuemNaoDecidiu_PodeExecutar()
    {
        var pedido = Pedido();
        var outroDecisor = Guid.CreateVersion7();

        pedido.MarkExecuted(
            Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB,
            [outroDecisor], DateTimeOffset.UtcNow);

        Assert.Equal(PaymentRequestStatus.Executed, pedido.Status);
    }

    [Fact]
    public void ExecutarDuasVezes_ERecusado()
    {
        var pedido = Pedido();
        pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], DateTimeOffset.UtcNow);

        var erro = Assert.Throws<InvalidOperationException>(() =>
            pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], DateTimeOffset.UtcNow));

        Assert.Contains("dobrar", erro.Message);
    }

    [Fact]
    public void ExecutarPedidoCancelado_ERecusado()
    {
        var pedido = Pedido();
        pedido.Cancel("Já não é preciso", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], DateTimeOffset.UtcNow));
    }

    /// <summary>O dinheiro saiu. Desfazer é outro movimento, não um cancelamento.</summary>
    [Fact]
    public void CancelarPedidoExecutado_ERecusado()
    {
        var pedido = Pedido();
        pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => pedido.Cancel("Engano", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CancelarSemMotivo_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => Pedido().Cancel("  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDoPedido()
    {
        var pedido = Pedido();
        pedido.MarkExecuted(Guid.CreateVersion7(), Guid.CreateVersion7(), PaymentMethod.TB, [], DateTimeOffset.UtcNow);

        Assert.Equal(0, pedido.Version);
    }

    // ---- extracto de conta ----

    /// <summary>
    /// A razão de ser do extracto: saldo e movimento nascem no mesmo acto, e
    /// nenhum caminho altera um sem o outro.
    /// </summary>
    [Fact]
    public void CadaMovimentoDeSaldoDeixaLinhaNoExtracto()
    {
        var conta = Conta(0m);

        conta.Deposit(200_000m, Instante, "Transferência do sócio");
        conta.Withdraw(114_000m, Instante.AddHours(1), "Pagamento a fornecedor");

        Assert.Equal(2, conta.Movements.Count);
        Assert.Equal(86_000m, conta.Balance);
    }

    [Fact]
    public void MovimentoCongelaOSaldoDepoisDeSi()
    {
        var conta = Conta(0m);

        conta.Deposit(200_000m, Instante, null);
        conta.Withdraw(114_000m, Instante.AddHours(1), "Pagamento");

        var movimentos = conta.Movements.ToList();

        Assert.Equal(200_000m, movimentos[0].BalanceAfter);
        Assert.Equal(86_000m, movimentos[1].BalanceAfter);

        // Depois um depósito. O saldo do movimento anterior não muda — é o que
        // torna o extracto legível a posteriori.
        conta.Deposit(14_000m, Instante.AddHours(2), null);

        Assert.Equal(86_000m, movimentos[1].BalanceAfter);
        Assert.Equal(100_000m, conta.Movements.Last().BalanceAfter);
    }

    [Fact]
    public void SentidoDoMovimentoSegueOTipoDeOperacao()
    {
        var conta = Conta(0m);

        conta.Deposit(1_000m, Instante, null);
        conta.Withdraw(400m, Instante.AddMinutes(1), "Pagamento");

        var movimentos = conta.Movements.ToList();

        Assert.Equal(BankMovementDirection.Credit, movimentos[0].Direction);
        Assert.Equal(BankMovementDirection.Debit, movimentos[1].Direction);

        // Valor sempre positivo: o sentido está na direcção, não no sinal.
        Assert.All(movimentos, m => Assert.True(m.Amount > 0));
    }

    /// <summary>
    /// Uma recusa não pode deixar rasto no extracto — senão o extracto passa a
    /// registar o que não aconteceu.
    /// </summary>
    [Fact]
    public void MovimentoRecusado_NaoEntraNoExtracto()
    {
        var conta = Conta(1_000m);
        var antes = conta.Movements.Count;

        Assert.Throws<InsufficientFundsException>(
            () => conta.Withdraw(5_000m, Instante, "Pagamento sem saldo"));

        Assert.Equal(antes, conta.Movements.Count);
        Assert.Equal(1_000m, conta.Balance);
    }

    /// <summary>
    /// O percurso de volta que a reconciliação precisa: do movimento ao
    /// documento que o causou.
    /// </summary>
    [Fact]
    public void MovimentoAponta_ADocumentoDeOrigem()
    {
        var conta = Conta(200_000m);
        var pedidoId = Guid.CreateVersion7();

        var movimento = conta.Withdraw(
            114_000m, Instante, "Pagamento a Sonangol",
            BankMovementSources.PaymentRequest, pedidoId);

        Assert.Equal(BankMovementSources.PaymentRequest, movimento.SourceType);
        Assert.Equal(pedidoId, movimento.SourceId);
    }

    [Fact]
    public void CarregamentoManual_NaoTemOrigemDocumental()
    {
        var conta = Conta(0m);

        var movimento = conta.Deposit(50_000m, Instante, "Depósito em numerário");

        Assert.Null(movimento.SourceType);
        Assert.Null(movimento.SourceId);
        Assert.Equal("Depósito em numerário", movimento.Description);
    }

    [Fact]
    public void DepositoSemDescricao_RecebeUmaPorOmissao()
    {
        var movimento = Conta(0m).Deposit(1m, Instante, "   ");

        Assert.False(string.IsNullOrWhiteSpace(movimento.Description));
    }
}
