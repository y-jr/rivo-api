using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// O papel dos documentos fiscais (ADR-066, fecha o K23).
///
/// <para>
/// O que aqui se testa não é o aspecto do PDF — isso é do compositor. É a
/// mecânica que garante que <strong>o papel de um documento é sempre o mesmo
/// papel</strong>, e que ninguém recebe um documento sem emitente identificado.
/// </para>
/// </summary>
public sealed class FiscalDocumentFileTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 18);

    private static readonly DateTimeOffset Agora = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static SalesInvoice Factura()
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");

        return SalesInvoice.Issue(
            serie.Allocate(), Hoje, Hoje, ClienteId,
            new InvoicedParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO"),
            "AOA",
            [new NewInvoiceLine("Serviço", 1m, 100_000m, "NOR", 14m)],
            "IVA - regime geral");
    }

    private static Receipt Recibo(SalesInvoice factura)
    {
        var serie = DocumentSeries.Open(DocumentType.RG, "S001");

        return Receipt.Register(
            serie.Allocate(), Hoje, ClienteId, factura.Customer, "AOA",
            PaymentMethod.TB,
            [new NewSettlement(factura.Id, factura.Number.ToString(), 50_000m)],
            null,
            null);
    }

    private static (IssueFiscalDocumentFile Caso,
        FakeFiscalDocumentRenderer Compositor,
        FakeFiscalDocumentArchive Arquivo,
        FakeFiscalDocumentFileStore Ficheiros,
        FakeAuditTrail Auditoria) Montar(
            FakeSalesInvoiceStore store,
            Rivo.Fiscal.Contracts.TaxEntityProfileView? emitente)
    {
        var compositor = new FakeFiscalDocumentRenderer();
        var arquivo = new FakeFiscalDocumentArchive();
        var ficheiros = new FakeFiscalDocumentFileStore();
        var auditoria = new FakeAuditTrail();

        var caso = new IssueFiscalDocumentFile(
            store, ficheiros, new FakeTaxEntityDirectory(emitente),
            compositor, arquivo, auditoria, new RelogioFixo(Agora));

        return (caso, compositor, arquivo, ficheiros, auditoria);
    }

    /// <summary>
    /// <strong>O caso que justifica metade deste trabalho.</strong> Antes do
    /// ADR-066 o sistema não sabia o seu próprio nome; compor uma factura sem
    /// emitente produziria um papel que parece uma factura e não é.
    /// </summary>
    [Fact]
    public async Task SemIdentidadeFiscalDeclarada_NaoCompoeNada()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, arquivo, _, _) = Montar(store, emitente: null);

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentFileOutcome.IssuerNotDeclared, resultado.Outcome);
        Assert.Equal(0, compositor.Calls);
        Assert.Equal(0, arquivo.Stores);
    }

    [Fact]
    public async Task DocumentoInexistente_NaoChegaAPerguntarPeloEmitente()
    {
        var store = new FakeSalesInvoiceStore();
        var directorio = new FakeTaxEntityDirectory(FakeTaxEntityDirectory.Emitente());

        var caso = new IssueFiscalDocumentFile(
            store, new FakeFiscalDocumentFileStore(), directorio,
            new FakeFiscalDocumentRenderer(), new FakeFiscalDocumentArchive(),
            new FakeAuditTrail(), new RelogioFixo(Agora));

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentFileOutcome.DocumentNotFound, resultado.Outcome);

        // A ordem importa: procurar o documento primeiro evita ir à
        // configuração por causa de um identificador que não existe.
        Assert.Equal(0, directorio.Calls);
    }

    [Fact]
    public async Task PrimeiraDescarga_CompoeGuardaEAudita()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, arquivo, ficheiros, auditoria) =
            Montar(store, FakeTaxEntityDirectory.Emitente());

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentFileOutcome.Ready, resultado.Outcome);
        Assert.Equal(1, compositor.Calls);
        Assert.Equal(1, arquivo.Stores);
        Assert.Equal(1, ficheiros.SaveCount);
        Assert.NotNull(resultado.Content);
        Assert.NotEmpty(resultado.Content!);

        // O nome do ficheiro não pode levar a barra do número do documento.
        Assert.DoesNotContain('/', resultado.FileName!);
        Assert.EndsWith(".pdf", resultado.FileName!, StringComparison.Ordinal);

        var registo = Assert.Single(auditoria.Records);
        Assert.Equal("finance.fiscal_document.file_generated", registo.Action);
        Assert.Contains("hash", registo.NewValue!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A propriedade central: duas descargas dão o mesmo ficheiro, e a segunda
    /// <strong>não compõe nada</strong>.
    /// </summary>
    [Fact]
    public async Task SegundaDescarga_DevolveOMesmoFicheiroSemCompor()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, arquivo, ficheiros, _) =
            Montar(store, FakeTaxEntityDirectory.Emitente());

        var primeira = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        var segunda = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(1, compositor.Calls);
        Assert.Equal(1, arquivo.Stores);
        Assert.Single(ficheiros.Ficheiros);
        Assert.Equal(primeira.Content, segunda.Content);
        Assert.Equal(primeira.ContentHash, segunda.ContentHash);
    }

    /// <summary>
    /// Anular obriga a papel novo: entregar uma factura anulada com o aspecto de
    /// uma boa é pior do que não a entregar. E o papel anterior fica — foi ele
    /// que circulou (BR-14).
    /// </summary>
    [Fact]
    public async Task DepoisDeAnulada_CompoePapelNovoESemApagarOAnterior()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, _, ficheiros, _) =
            Montar(store, FakeTaxEntityDirectory.Emitente());

        var antes = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        factura.Cancel("Erro de facturação", Agora);

        var depois = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(2, compositor.Calls);
        Assert.NotEqual(antes.Content, depois.Content);

        Assert.Equal(2, ficheiros.Ficheiros.Count);
        Assert.Contains(ficheiros.Ficheiros, f => !f.ReflectsCancellation);
        Assert.Contains(ficheiros.Ficheiros, f => f.ReflectsCancellation);

        // O papel novo diz que está anulado; o compositor recebeu-o assim.
        Assert.True(compositor.Printouts[1].Document.Cancelled);
        Assert.Equal("Erro de facturação", compositor.Printouts[1].Document.CancellationReason);
    }

    /// <summary>
    /// K12: a linha aponta a um ficheiro que desapareceu do armazenamento.
    /// Compõe-se de novo em vez de responder erro.
    /// </summary>
    [Fact]
    public async Task FicheiroOrfao_ERecomposto()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, arquivo, _, _) =
            Montar(store, FakeTaxEntityDirectory.Emitente());

        var primeira = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        arquivo.Perder(primeira.StoredDocumentId!.Value);

        var segunda = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentFileOutcome.Ready, segunda.Outcome);
        Assert.Equal(2, compositor.Calls);
        Assert.Equal(2, arquivo.Stores);
    }

    /// <summary>
    /// O recibo não tem imposto — liquida valores já tributados. A projecção
    /// tem de dizer isso ao compositor, senão saem colunas de zeros.
    /// </summary>
    [Fact]
    public async Task Recibo_ProjectaSemImpostoEComMeioDePagamento()
    {
        var factura = Factura();
        var recibo = Recibo(factura);
        var store = new FakeSalesInvoiceStore().With(factura).With(recibo);
        var (caso, compositor, _, _, _) = Montar(store, FakeTaxEntityDirectory.Emitente());

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.Receipt, recibo.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentFileOutcome.Ready, resultado.Outcome);

        var papel = Assert.Single(compositor.Printouts).Document;
        Assert.Equal("Recibo", papel.Title);
        Assert.Equal(0m, papel.TaxTotal);
        Assert.Equal("TB", papel.PaymentMethod);
        Assert.Null(papel.TaxPointDate);
        Assert.Equal(50_000m, papel.GrossTotal);

        // A linha diz que factura liquidou.
        Assert.Contains(factura.Number.ToString(), Assert.Single(papel.Lines).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MencaoFiscalVaiComoFoiCongelada()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, compositor, _, _, _) = Montar(store, FakeTaxEntityDirectory.Emitente());

        await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal("IVA - regime geral", Assert.Single(compositor.Printouts).Document.FiscalNotice);
    }
}

/// <summary>
/// A entrega ao cliente. O que importa testar é <strong>para onde</strong> vai —
/// e que não vai para onde o pedido disser.
/// </summary>
public sealed class FiscalDocumentDeliveryTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 18);

    private static readonly DateTimeOffset Agora = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static SalesInvoice Factura(bool consumidorFinal = false)
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");

        return consumidorFinal
            ? SalesInvoice.Issue(
                serie.Allocate(), Hoje, Hoje, null,
                InvoicedParty.FinalConsumer("CONSUMIDORFINAL", "Consumidor final"),
                "AOA", [new NewInvoiceLine("Serviço", 1m, 100_000m, "NOR", 14m)])
            : SalesInvoice.Issue(
                serie.Allocate(), Hoje, Hoje, ClienteId,
                new InvoicedParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO"),
                "AOA", [new NewInvoiceLine("Serviço", 1m, 100_000m, "NOR", 14m)]);
    }

    private static CustomerReference Cliente(string? email) =>
        new(ClienteId, "Refriango", "5417654321", CustomerStatus.Active,
            new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"),
            null,
            email);

    private static (DeliverFiscalDocument Caso, FakeFiscalDocumentDelivery Canal, FakeAuditTrail Auditoria) Montar(
        FakeSalesInvoiceStore store,
        CustomerReference? cliente,
        string? erroDeEnvio = null)
    {
        var auditoria = new FakeAuditTrail();
        var canal = new FakeFiscalDocumentDelivery(erroDeEnvio);
        var directorio = new FakeTaxEntityDirectory(FakeTaxEntityDirectory.Emitente());

        var ficheiros = new IssueFiscalDocumentFile(
            store, new FakeFiscalDocumentFileStore(), directorio,
            new FakeFiscalDocumentRenderer(), new FakeFiscalDocumentArchive(),
            auditoria, new RelogioFixo(Agora));

        var caso = new DeliverFiscalDocument(
            ficheiros, store, new FakeCustomerDirectory(cliente), directorio, canal, auditoria);

        return (caso, canal, auditoria);
    }

    [Fact]
    public async Task ClienteComEndereco_RecebeOAnexo()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, canal, auditoria) = Montar(store, Cliente("financeiro@refriango.ao"));

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentDeliveryOutcome.Sent, resultado.Outcome);

        var enviado = Assert.Single(canal.Sent);
        Assert.Equal("financeiro@refriango.ao", enviado.ToAddress);
        Assert.Equal("Refriango", enviado.ToName);
        Assert.Contains(factura.Number.ToString(), enviado.Subject, StringComparison.Ordinal);
        Assert.NotEmpty(enviado.Content);
        Assert.EndsWith(".pdf", enviado.FileName, StringComparison.Ordinal);

        Assert.Contains(auditoria.Records, r => r.Action == "finance.fiscal_document.delivered");
    }

    [Fact]
    public async Task ClienteSemEndereco_NaoEnviaEExplica()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, canal, _) = Montar(store, Cliente(email: null));

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentDeliveryOutcome.NoRecipient, resultado.Outcome);
        Assert.Empty(canal.Sent);
        Assert.Contains("Refriango", resultado.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Consumidor final não tem cliente registado. Não é erro de configuração —
    /// é um documento que se entrega em mão.
    /// </summary>
    [Fact]
    public async Task ConsumidorFinal_NaoTemAQuemEnviar()
    {
        var factura = Factura(consumidorFinal: true);
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, canal, _) = Montar(store, cliente: null);

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentDeliveryOutcome.NoRecipient, resultado.Outcome);
        Assert.Empty(canal.Sent);
        Assert.Contains("consumidor final", resultado.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Um envio falhado é informação: alguém tentou e o cliente não recebeu. Fica
    /// na trilha, com o destino.
    /// </summary>
    [Fact]
    public async Task EnvioFalhado_SobeOErroEFicaNaAuditoria()
    {
        var factura = Factura();
        var store = new FakeSalesInvoiceStore().With(factura);
        var (caso, _, auditoria) = Montar(
            store, Cliente("financeiro@refriango.ao"), erroDeEnvio: "550 mailbox unavailable");

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, factura.Id, Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentDeliveryOutcome.DeliveryFailed, resultado.Outcome);
        Assert.Equal("550 mailbox unavailable", resultado.Error);

        var entrega = Assert.Single(
            auditoria.Records, r => r.Action == "finance.fiscal_document.delivered");

        Assert.Contains("\"sent\":false", entrega.NewValue!, StringComparison.Ordinal);
        Assert.Contains("financeiro@refriango.ao", entrega.NewValue!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocumentoInexistente_NaoEnviaNada()
    {
        var store = new FakeSalesInvoiceStore();
        var (caso, canal, _) = Montar(store, Cliente("financeiro@refriango.ao"));

        var resultado = await caso.ExecuteAsync(
            FiscalDocumentKind.SalesInvoice, Guid.CreateVersion7(), Contexto, CancellationToken.None);

        Assert.Equal(FiscalDocumentDeliveryOutcome.DocumentNotFound, resultado.Outcome);
        Assert.Empty(canal.Sent);
    }
}
