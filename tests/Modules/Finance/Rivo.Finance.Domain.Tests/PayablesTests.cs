using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Contas a Pagar e Tesouraria. É onde BR-1, BR-3, BR-5 e BR-17 se encontram.
/// </summary>
public class PayablesTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);

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
            conta.Deposit(saldo);
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

        var erro = Assert.Throws<InsufficientFundsException>(() => conta.Withdraw(1_000.01m));

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
        conta.Withdraw(1_000m);

        Assert.Equal(0m, conta.Balance);
    }

    [Fact]
    public void ContaFechada_NaoMovimenta()
    {
        var conta = Conta();
        conta.Close();

        Assert.Throws<InvalidOperationException>(() => conta.Withdraw(1m));
        Assert.Throws<InvalidOperationException>(() => conta.Deposit(1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MovimentoNaoPositivo_ERecusado(decimal valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Conta().Withdraw(valor));
        Assert.Throws<ArgumentOutOfRangeException>(() => Conta().Deposit(valor));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDaConta()
    {
        var conta = Conta();
        conta.Withdraw(100m);

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
}
