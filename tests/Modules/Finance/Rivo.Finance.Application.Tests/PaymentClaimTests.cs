using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Pedido de confirmação de pagamento (ADR-044) — sem gateway, o cliente diz
/// que pagou e `finance` confirma, reaproveitando <see cref="RegisterReceipt"/>
/// tal como está.
/// </summary>
public class PaymentClaimTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 3);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static readonly Guid OutroClienteId = Guid.CreateVersion7();

    private static readonly DateTimeOffset Agora = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    private static PostDocument SemPostagem() => new(new FakeLedgerStore());

    private static RegisterReceipt Recebimento(FakeSalesInvoiceStore store) =>
        new(store, new FakeAuditTrail(), SemPostagem(), new RelogioFixo(Agora), Opcoes.Financeiras());

    private static SalesInvoice FacturaDe(Guid clienteId, decimal bruto = 114_000m)
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");
        var liquido = bruto / 1.14m;

        return SalesInvoice.Issue(
            serie.Allocate(), Hoje, Hoje, clienteId,
            new InvoicedParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO"),
            "AOA",
            [new NewInvoiceLine("Serviço", 1m, decimal.Round(liquido, 2), "NOR", 14m)]);
    }

    // ---- submissão ----

    [Fact]
    public async Task SubmitPaymentClaim_FacturaDoProprioCliente_EAceite()
    {
        var factura = FacturaDe(ClienteId);
        var store = new FakeSalesInvoiceStore().With(factura);
        var documentId = Guid.CreateVersion7();
        var documents = new FakeDocumentCatalogue().With(documentId);
        var submit = new SubmitPaymentClaim(store, documents, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await submit.ExecuteAsync(
            ClienteId, factura.Id, 114_000m, Hoje, documentId, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentClaimOutcome.Submitted, resultado.Outcome);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task SubmitPaymentClaim_FacturaDeOutroCliente_EDesconhecida()
    {
        var factura = FacturaDe(ClienteId);
        var store = new FakeSalesInvoiceStore().With(factura);
        var documentId = Guid.CreateVersion7();
        var documents = new FakeDocumentCatalogue().With(documentId);
        var submit = new SubmitPaymentClaim(store, documents, new FakeAuditTrail(), new RelogioFixo(Agora));

        // Um cliente a tentar pagar a factura de outro — a mesma resposta que
        // "não existe": não se revela que a factura pertence a terceiros.
        var resultado = await submit.ExecuteAsync(
            OutroClienteId, factura.Id, 114_000m, Hoje, documentId, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentClaimOutcome.InvoiceNotFound, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SubmitPaymentClaim_ExcedeOEmAberto_ERecusado()
    {
        var factura = FacturaDe(ClienteId, 100_000m);
        var store = new FakeSalesInvoiceStore().With(factura);
        var documentId = Guid.CreateVersion7();
        var documents = new FakeDocumentCatalogue().With(documentId);
        var submit = new SubmitPaymentClaim(store, documents, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await submit.ExecuteAsync(
            ClienteId, factura.Id, 999_999m, Hoje, documentId, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentClaimOutcome.ExceedsOutstanding, resultado.Outcome);
    }

    [Fact]
    public async Task SubmitPaymentClaim_ComprovativoInexistente_ERecusado()
    {
        var factura = FacturaDe(ClienteId);
        var store = new FakeSalesInvoiceStore().With(factura);
        var submit = new SubmitPaymentClaim(
            store, new FakeDocumentCatalogue(), new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await submit.ExecuteAsync(
            ClienteId, factura.Id, 114_000m, Hoje, Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            CancellationToken.None);

        Assert.Equal(SubmitPaymentClaimOutcome.DocumentNotFound, resultado.Outcome);
    }

    // ---- confirmação ----

    [Fact]
    public async Task ConfirmPaymentClaim_PedidoPendente_RegistaORecibo()
    {
        var factura = FacturaDe(ClienteId);
        var pedido = PaymentClaim.Submit(
            factura.Id, ClienteId, 114_000m, Hoje, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Agora);

        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG).With(factura).With(pedido);
        var confirm = new ConfirmPaymentClaim(store, Recebimento(store), new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await confirm.ExecuteAsync(pedido.Id, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(ReviewPaymentClaimOutcome.Confirmed, resultado.Outcome);
        Assert.NotNull(resultado.ReceiptId);
    }

    [Fact]
    public async Task ConfirmPaymentClaim_JaConfirmado_ERecusado()
    {
        var factura = FacturaDe(ClienteId);
        var pedido = PaymentClaim.Submit(
            factura.Id, ClienteId, 114_000m, Hoje, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Agora);
        pedido.Confirm(Guid.CreateVersion7(), Guid.CreateVersion7(), Agora);

        var store = new FakeSalesInvoiceStore().WithSeries(DocumentType.RG).With(factura).With(pedido);
        var confirm = new ConfirmPaymentClaim(store, Recebimento(store), new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await confirm.ExecuteAsync(pedido.Id, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(ReviewPaymentClaimOutcome.Rejected, resultado.Outcome);
    }

    [Fact]
    public async Task ConfirmPaymentClaim_SemSerieAberta_FalhaEReciboEPendente()
    {
        var factura = FacturaDe(ClienteId);
        var pedido = PaymentClaim.Submit(
            factura.Id, ClienteId, 114_000m, Hoje, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Agora);

        // Sem WithSeries(RG) — mesmo cenário de RegisterReceiptOutcome.SeriesNotFound.
        var store = new FakeSalesInvoiceStore().With(factura).With(pedido);
        var confirm = new ConfirmPaymentClaim(store, Recebimento(store), new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await confirm.ExecuteAsync(pedido.Id, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(ReviewPaymentClaimOutcome.ReceiptFailed, resultado.Outcome);
        Assert.Equal(PaymentClaimStatus.Pending, pedido.Status);
    }

    // ---- rejeição ----

    [Fact]
    public async Task RejectPaymentClaim_PedidoPendente_FicaRejeitadoComMotivo()
    {
        var pedido = PaymentClaim.Submit(
            Guid.CreateVersion7(), ClienteId, 100_000m, Hoje, Guid.CreateVersion7(), Guid.CreateVersion7(), null, Agora);

        var store = new FakeSalesInvoiceStore().With(pedido);
        var reject = new RejectPaymentClaim(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await reject.ExecuteAsync(
            pedido.Id, "Comprovativo ilegível.", Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(ReviewPaymentClaimOutcome.RejectedOk, resultado.Outcome);
        Assert.Equal(PaymentClaimStatus.Rejected, pedido.Status);
        Assert.Equal("Comprovativo ilegível.", pedido.RejectionReason);
    }

    [Fact]
    public async Task RejectPaymentClaim_Inexistente_ENotFound()
    {
        var store = new FakeSalesInvoiceStore();
        var reject = new RejectPaymentClaim(store, new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await reject.ExecuteAsync(
            Guid.CreateVersion7(), "Motivo", Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(ReviewPaymentClaimOutcome.NotFound, resultado.Outcome);
    }
}
