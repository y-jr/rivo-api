using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;
using Rivo.Procurement.Contracts;

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
            "FT 661054", null, null, new PayeeParty("Sonangol Distribuidora", "5401234567"),
            Hoje.AddDays(-5), Hoje.AddDays(25), "AOA", liquido, imposto, "Combustível");

    private static CreatePaymentRequest Pedir(
        FakePayablesStore store,
        FakePaymentApproval approval,
        FakePlanningStore? planning = null) =>
        new(store, planning ?? new FakePlanningStore(), approval, new FakeAuditTrail());

    // ---- Registar factura de compra: ligação ao Fornecedor ----

    private static RegisterPurchaseInvoice Registar(
        FakePayablesStore store, SupplierReference? fornecedor = null, PurchaseOrderReference? ordem = null) =>
        new(store, new FakeSupplierDirectory(fornecedor), new FakePurchaseOrderDirectory(ordem), new FakeAuditTrail(),
            new PostDocument(new FakeLedgerStore()), new RelogioFixo(Agora));

    [Fact]
    public async Task SupplierIdIndicado_EEncontrado_LigaAFactura()
    {
        var fornecedor = new SupplierReference(
            Guid.CreateVersion7(), "Sonangol Distribuidora", "5401234567", null, SupplierStatus.Active);
        var store = new FakePayablesStore();

        var resultado = await Registar(store, fornecedor).ExecuteAsync(
            "FT 661054", fornecedor.SupplierId, null, fornecedor.Name, fornecedor.TaxId,
            Hoje, Hoje.AddDays(30), "AOA", 100_000m, 14_000m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var compra = await store.FindPurchaseInvoiceAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);
        Assert.Equal(fornecedor.SupplierId, compra!.SupplierId);
    }

    /// <summary>
    /// Quem chama afirmou uma ligação que não existe — não é estado a ignorar
    /// em silêncio, é a factura a apontar para um fornecedor inventado.
    /// </summary>
    [Fact]
    public async Task SupplierIdIndicado_NaoExisteEmProcurement_ERecusado()
    {
        var store = new FakePayablesStore();

        var resultado = await Registar(store).ExecuteAsync(
            "FT 661054", Guid.CreateVersion7(), null, "Sonangol Distribuidora", "5401234567",
            Hoje, Hoje.AddDays(30), "AOA", 100_000m, 14_000m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// O caso comum: quem regista tem a factura em papel, não o identificador
    /// — só o NIF. <see cref="ISupplierDirectory.FindByTaxIdAsync"/> existe
    /// para isto.
    /// </summary>
    [Fact]
    public async Task SemSupplierId_NifCoincideComFornecedorQualificado_LigaAutomaticamente()
    {
        var fornecedor = new SupplierReference(
            Guid.CreateVersion7(), "Sonangol Distribuidora", "5401234567", null, SupplierStatus.Active);
        var store = new FakePayablesStore();

        var resultado = await Registar(store, fornecedor).ExecuteAsync(
            "FT 661054", supplierId: null, null, fornecedor.Name, fornecedor.TaxId,
            Hoje, Hoje.AddDays(30), "AOA", 100_000m, 14_000m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var compra = await store.FindPurchaseInvoiceAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);
        Assert.Equal(fornecedor.SupplierId, compra!.SupplierId);
    }

    /// <summary>
    /// Nem toda a despesa passa por um Fornecedor qualificado em `procurement`
    /// — uma factura de electricidade não tem quem qualificar. Não encontrar
    /// não é erro.
    /// </summary>
    [Fact]
    public async Task SemSupplierId_NifSemCorrespondencia_RegistaSemLigacao()
    {
        var store = new FakePayablesStore();

        var resultado = await Registar(store).ExecuteAsync(
            "FT 8821", supplierId: null, null, "ENDE - Distribuição de Electricidade", "5417654321",
            Hoje, Hoje.AddDays(30), "AOA", 40_000m, 5_600m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var compra = await store.FindPurchaseInvoiceAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);
        Assert.Null(compra!.SupplierId);
    }

    // ---- Registar factura de compra: ligação à Ordem de Compra (3-way match) ----

    private static PurchaseOrderReference Ordem(Guid supplierId, params (decimal Encomendado, decimal Recebido, decimal Preco)[] linhas) =>
        new(
            Guid.CreateVersion7(), supplierId, "AOA",
            linhas.Sum(l => l.Encomendado * l.Preco),
            PurchaseOrderReferenceStatus.Issued,
            [.. linhas.Select(l => new PurchaseOrderLineReference(
                Guid.CreateVersion7(), "Cadeiras", l.Encomendado, l.Recebido, l.Preco, l.Encomendado * l.Preco))]);

    [Fact]
    public async Task PurchaseOrderIdIndicado_EDoMesmoFornecedor_LigaAOrdem()
    {
        var fornecedor = new SupplierReference(
            Guid.CreateVersion7(), "Angoferragens", "5402123456", null, SupplierStatus.Active);
        var ordem = Ordem(fornecedor.SupplierId, (10m, 10m, 9000m));
        var store = new FakePayablesStore();

        var resultado = await Registar(store, fornecedor, ordem).ExecuteAsync(
            "FT 9001", fornecedor.SupplierId, ordem.PurchaseOrderId, fornecedor.Name, fornecedor.TaxId,
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var compra = await store.FindPurchaseInvoiceAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);
        Assert.Equal(ordem.PurchaseOrderId, compra!.PurchaseOrderId);
    }

    /// <summary>
    /// Sem fornecedor indicado nem ligável pelo NIF, mas a ordem sabe-o com
    /// certeza — herda-o dela em vez de deixar a factura por ligar.
    /// </summary>
    [Fact]
    public async Task PurchaseOrderIdIndicado_SemFornecedorConhecido_HerdaOFornecedorDaOrdem()
    {
        var fornecedorId = Guid.CreateVersion7();
        var ordem = Ordem(fornecedorId, (10m, 10m, 9000m));
        var store = new FakePayablesStore();

        var resultado = await Registar(store, fornecedor: null, ordem).ExecuteAsync(
            "FT 9001", supplierId: null, ordem.PurchaseOrderId, "Angoferragens", "5402123456",
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var compra = await store.FindPurchaseInvoiceAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);
        Assert.Equal(fornecedorId, compra!.SupplierId);
    }

    [Fact]
    public async Task PurchaseOrderIdIndicado_NaoExisteEmProcurement_ERecusado()
    {
        var store = new FakePayablesStore();

        var resultado = await Registar(store).ExecuteAsync(
            "FT 9001", null, Guid.CreateVersion7(), "Angoferragens", "5402123456",
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// A ordem existe, mas é de outro fornecedor — uma factura não pode
    /// acertar uma encomenda que não é dela.
    /// </summary>
    [Fact]
    public async Task PurchaseOrderIdIndicado_DeOutroFornecedor_ERecusado()
    {
        var fornecedor = new SupplierReference(
            Guid.CreateVersion7(), "Angoferragens", "5402123456", null, SupplierStatus.Active);
        var ordem = Ordem(Guid.CreateVersion7(), (10m, 10m, 9000m));
        var store = new FakePayablesStore();

        var resultado = await Registar(store, fornecedor, ordem).ExecuteAsync(
            "FT 9001", fornecedor.SupplierId, ordem.PurchaseOrderId, fornecedor.Name, fornecedor.TaxId,
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m, null,
            Contexto, CancellationToken.None);

        Assert.Equal(RegisterPurchaseInvoiceOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    // ---- O 3-way match: só os totais lado a lado, sem decidir se "bate" ----

    private static GetPurchaseInvoiceMatch Comparar(FakePayablesStore store, PurchaseOrderReference? ordem = null) =>
        new(store, new FakePurchaseOrderDirectory(ordem));

    [Fact]
    public async Task FacturaSemOrdemLigada_DevolveSoOFacturado()
    {
        var compra = Compra();
        var store = new FakePayablesStore().With(compra);

        var vista = await Comparar(store).ExecuteAsync(compra.Id, CancellationToken.None);

        Assert.Null(vista!.PurchaseOrderId);
        Assert.Null(vista.OrderedTotal);
        Assert.Null(vista.ReceivedTotal);
        Assert.Equal(compra.NetTotal, vista.InvoicedNetTotal);
        Assert.Empty(vista.Lines);
    }

    /// <summary>
    /// O caso que dá nome à suite: os três números lado a lado. Aqui batem —
    /// mas o caso de uso não afirma isso, só devolve os números; é quem olha
    /// que decide se bate.
    /// </summary>
    [Fact]
    public async Task FacturaComOrdemLigada_DevolveEncomendadoRecebidoEFacturado()
    {
        var fornecedorId = Guid.CreateVersion7();
        var ordem = Ordem(fornecedorId, (10m, 10m, 9000m));
        var compra = PurchaseInvoice.Register(
            "FT 9001", fornecedorId, ordem.PurchaseOrderId, new PayeeParty("Angoferragens", "5402123456"),
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m);
        var store = new FakePayablesStore().With(compra);

        var vista = await Comparar(store, ordem).ExecuteAsync(compra.Id, CancellationToken.None);

        Assert.Equal(ordem.PurchaseOrderId, vista!.PurchaseOrderId);
        Assert.Equal(90_000m, vista.OrderedTotal);
        Assert.Equal(90_000m, vista.ReceivedTotal);
        Assert.Equal(90_000m, vista.InvoicedNetTotal);
        Assert.Single(vista.Lines);
    }

    /// <summary>
    /// Recebido a menos do que facturado — o caso que o 3-way match existe
    /// para apanhar. Não é recusado: fica visível nos números, não bloqueado.
    /// </summary>
    [Fact]
    public async Task FacturaAcimaDoRecebido_NaoEBloqueada_MasFicaVisivelNoMatch()
    {
        var fornecedorId = Guid.CreateVersion7();
        var ordem = Ordem(fornecedorId, (10m, 6m, 9000m));
        var store = new FakePayablesStore();

        var resultado = await Registar(store, ordem: ordem).ExecuteAsync(
            "FT 9001", supplierId: null, ordem.PurchaseOrderId, "Angoferragens", "5402123456",
            Hoje, Hoje.AddDays(30), "AOA", 90_000m, 0m, null,
            Contexto, CancellationToken.None);
        Assert.Equal(RegisterPurchaseInvoiceOutcome.Registered, resultado.Outcome);

        var vista = await Comparar(store, ordem).ExecuteAsync(resultado.PurchaseInvoiceId!.Value, CancellationToken.None);

        Assert.Equal(90_000m, vista!.OrderedTotal);
        Assert.Equal(54_000m, vista.ReceivedTotal);
        Assert.Equal(90_000m, vista.InvoicedNetTotal);
    }

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
