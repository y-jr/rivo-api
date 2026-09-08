using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// A factura de venda tem a forma do documento fiscal e não a conformidade —
/// ADR-036. As invariantes testadas aqui são as que sobrevivem à ausência de
/// certificação, e são as caras de enxertar depois.
/// </summary>
public class SalesInvoiceTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 24);

    private static InvoicedParty Cliente() =>
        new("Kianda Lda", "5417000000", "Rua Rainha Ginga 12", "Luanda", "AO");

    private static DocumentNumber Numero() => DocumentSeries.Open(DocumentType.FT, "S001").Allocate();

    private static SalesInvoice Emitida(params NewInvoiceLine[] linhas) =>
        FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            linhas.Length > 0 ? linhas : [new NewInvoiceLine("Consultoria", 1, 100_000m, "NOR", 14m, "ART-TESTE", "UN")]);

    [Fact]
    public void FacturaEmitida_NasceNormal()
    {
        Assert.Equal(InvoiceStatus.Normal, Emitida().Status);
    }

    [Fact]
    public void NumeroTemAFormaDoSaft()
    {
        Assert.Equal("FT S001/1", Emitida().Number.Formatted);
    }

    // ---- totais ----

    [Fact]
    public void TotaisSaoASomaDasLinhas()
    {
        var factura = Emitida(
            new NewInvoiceLine("Consultoria", 2, 50_000m, "NOR", 14m, "ART-TESTE", "UN"),
            new NewInvoiceLine("Deslocacao", 1, 10_000m, "NOR", 14m, "ART-TESTE", "UN"));

        Assert.Equal(110_000m, factura.NetTotal);
        Assert.Equal(15_400m, factura.TaxTotal);
        Assert.Equal(125_400m, factura.GrossTotal);
    }

    /// <summary>
    /// O valor exportado tem de ser o mesmo que o documento mostra. Arredondar
    /// só na apresentação faria a soma das linhas visíveis não bater com o
    /// total gravado.
    /// </summary>
    [Fact]
    public void ArredondamentoEPorLinha_ADuasCasas()
    {
        var factura = Emitida(new NewInvoiceLine("Servico", 3, 33.333m, "NOR", 14m, "ART-TESTE", "UN"));

        // 3 x 33,333 = 99,999 -> 100,00 ; 100,00 x 14% = 14,00
        Assert.Equal(100.00m, factura.NetTotal);
        Assert.Equal(14.00m, factura.TaxTotal);
    }

    [Fact]
    public void LinhaIsentaNaoLiquidaImposto()
    {
        var factura = Emitida(new NewInvoiceLine("Servico isento", 1, 50_000m, "ISE", 0m, "ART-TESTE", "UN"));

        Assert.Equal(50_000m, factura.NetTotal);
        Assert.Equal(0m, factura.TaxTotal);
        Assert.Equal(50_000m, factura.GrossTotal);
    }

    [Fact]
    public void LinhasSaoNumeradasAPartirDeUm()
    {
        var factura = Emitida(
            new NewInvoiceLine("A", 1, 10m, "NOR", 14m, "ART-TESTE", "UN"),
            new NewInvoiceLine("B", 1, 10m, "NOR", 14m, "ART-TESTE", "UN"));

        Assert.Equal([1, 2], factura.Lines.Select(l => l.LineNumber));
    }

    // ---- recusas na emissão ----

    [Fact]
    public void FacturaSemLinhas_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA", []));
    }

    [Fact]
    public void FacturaSemCliente_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, Guid.Empty, Cliente(), "AOA",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    [Theory]
    [InlineData("Kwanza")]
    [InlineData("AO")]
    public void MoedaQueNaoEIso4217_ERecusada(string moeda)
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), moeda,
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    /// <summary>
    /// Uma factura emitida hoje pode cobrir um serviço de Dezembro — mas não um
    /// facto que ainda não aconteceu.
    /// </summary>
    [Fact]
    public void FactoGeradorPosteriorAoDocumento_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje.AddDays(1), Guid.CreateVersion7(), Cliente(), "AOA",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    [Fact]
    public void FactoGeradorAnteriorAoDocumento_EAceite()
    {
        var factura = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje.AddMonths(-1), Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]);

        Assert.Equal(Hoje.AddMonths(-1), factura.TaxPointDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuantidadeNaoPositiva_ERecusada(decimal quantidade)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Emitida(new NewInvoiceLine("X", quantidade, 10m, "NOR", 14m, "ART-TESTE", "UN")));
    }

    [Fact]
    public void PrecoNegativo_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Emitida(new NewInvoiceLine("X", 1, -10m, "NOR", 14m, "ART-TESTE", "UN")));
    }

    [Fact]
    public void PrecoZero_EAceite()
    {
        // Uma linha de oferta tem preço zero e continua a ser linha do documento.
        Assert.Equal(0m, Emitida(new NewInvoiceLine("Oferta", 1, 0m, "NOR", 14m, "ART-TESTE", "UN")).NetTotal);
    }

    [Fact]
    public void LinhaSemDescricao_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => Emitida(new NewInvoiceLine(" ", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")));
    }

    [Fact]
    public void LinhaSemCodigoDeImposto_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => Emitida(new NewInvoiceLine("X", 1, 10m, "", 14m, "ART-TESTE", "UN")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LinhaSemCodigoDeArtigo_ERecusada(string codigo)
    {
        // `ProductCode` é obrigatório em cada linha do SAF-T, e não se
        // reconstrói a partir da descrição em texto livre.
        Assert.Throws<ArgumentException>(() =>
            Emitida(new NewInvoiceLine("X", 1, 10m, "NOR", 14m, codigo, "UN")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LinhaSemUnidadeDeMedida_ERecusada(string unidade)
    {
        // Sem ela, meses depois ninguém sabe se as "3" eram horas, quilos ou
        // unidades — e o documento entregue ao cliente dizia-o.
        Assert.Throws<ArgumentException>(() =>
            Emitida(new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", unidade)));
    }

    [Fact]
    public void OCodigoDoArtigo_ENormalizadoParaMaiusculas()
    {
        // O mesmo artigo escrito "cim-42" e "CIM-42" apareceria como dois
        // produtos no ficheiro, e `ProductCodeConstraint` exige unicidade.
        var factura = Emitida(new NewInvoiceLine("Cimento", 1, 10m, "NOR", 14m, " cim-42 ", "sc"));

        Assert.Equal("CIM-42", factura.Lines[0].ProductCode);
    }

    [Fact]
    public void AUnidadeDeMedida_NaoENormalizada()
    {
        // Ao contrário do código: "kg" e "KG" são a mesma coisa para quem lê,
        // e maiusculizar mudaria o que o documento entregue ao cliente mostrava.
        var factura = Emitida(new NewInvoiceLine("Cimento", 1, 10m, "NOR", 14m, "CIM-42", " kg "));

        Assert.Equal("kg", factura.Lines[0].UnitOfMeasure);
    }

    [Fact]
    public void OCodigoDoArtigoNaoEntraNoHash()
    {
        // ⚠ Deliberado, e vale a pena fixá-lo: o hash cobre as linhas através
        // do total (ADR-060). Se um dia passar a cobri-las uma a uma, este
        // caso falha e obriga a decidir de propósito em vez de por acidente —
        // a cadeia de todas as facturas emitidas mudaria de valor.
        var comUm = Emitida(new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-A", "UN"));
        var comOutro = Emitida(new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-B", "UN"));

        Assert.Equal(comUm.Hash, comOutro.Hash);
    }

    // ---- cliente congelado ----

    /// <summary>
    /// O conteúdo da factura é facto histórico. Resolver o cliente ao vivo faria
    /// uma correcção de nome reescrever retroactivamente as facturas passadas.
    /// </summary>
    [Fact]
    public void ClienteFicaCongeladoNaFactura()
    {
        var factura = Emitida();

        Assert.Equal("Kianda Lda", factura.Customer.Name);
        Assert.Equal("5417000000", factura.Customer.TaxId);

        // E o identificador continua lá, para quem quiser o cliente de hoje.
        Assert.NotEqual(Guid.Empty, factura.CustomerId);
    }

    [Fact]
    public void ClienteSemNif_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            new InvoicedParty("Kianda Lda", "", "Rua X", "Luanda", "AO"));
    }

    [Fact]
    public void PaisDoClienteQueNaoEAlpha2_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            new InvoicedParty("Kianda Lda", "5417", "Rua X", "Luanda", "Angola"));
    }

    // ---- anulação ----

    [Fact]
    public void AnularMudaOEstadoEGuardaOMotivo()
    {
        var factura = Emitida();
        var quando = DateTimeOffset.UtcNow;

        factura.Cancel("Emitida ao cliente errado", quando);

        Assert.Equal(InvoiceStatus.Cancelled, factura.Status);
        Assert.Equal("Emitida ao cliente errado", factura.CancellationReason);
        Assert.Equal(quando, factura.CancelledAt);
    }

    [Fact]
    public void AnularSemMotivo_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => Emitida().Cancel("  ", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Não é idempotente de propósito: anular duas vezes é engano, e o segundo
    /// motivo apagaria o primeiro sem rasto.
    /// </summary>
    [Fact]
    public void AnularDuasVezes_ERecusado()
    {
        var factura = Emitida();
        factura.Cancel("Primeira razao", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            factura.Cancel("Segunda razao", DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Anular não apaga: os valores e as linhas continuam lá (BR-14). É a
    /// diferença entre anulação lógica e eliminação.
    /// </summary>
    [Fact]
    public void FacturaAnulada_MantemLinhasETotais()
    {
        var factura = Emitida(new NewInvoiceLine("Consultoria", 1, 100_000m, "NOR", 14m, "ART-TESTE", "UN"));
        factura.Cancel("Engano", DateTimeOffset.UtcNow);

        Assert.Single(factura.Lines);
        Assert.Equal(114_000m, factura.GrossTotal);
    }

    // ---- consumidor final ----

    private static SalesInvoice EmitidaAConsumidorFinal() =>
        FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, null,
            InvoicedParty.FinalConsumer("CONSUMIDORFINAL", "Consumidor final"),
            "AOA",
            [new NewInvoiceLine("Servico", 1, 5_000m, "NOR", 14m, "ART-TESTE", "UN")]);

    [Fact]
    public void ConsumidorFinal_NaoTemClienteRegistado()
    {
        var factura = EmitidaAConsumidorFinal();

        Assert.Null(factura.CustomerId);
        Assert.True(factura.Customer.IsFinalConsumer);
        Assert.Equal("CONSUMIDORFINAL", factura.Customer.TaxId);
    }

    /// <summary>
    /// A morada fica vazia porque **não existe**, não porque falta preencher.
    /// Quem não se identifica também não dá morada.
    /// </summary>
    [Fact]
    public void ConsumidorFinal_NaoTemMorada()
    {
        var cliente = EmitidaAConsumidorFinal().Customer;

        Assert.Equal(string.Empty, cliente.AddressDetail);
        Assert.Equal(string.Empty, cliente.City);
    }

    /// <summary>
    /// O identificador do consumidor final vem de configuração. Fixá-lo no
    /// domínio dar-lhe-ia ar de código oficial verificado, que não é.
    /// </summary>
    [Fact]
    public void ConsumidorFinal_SemIdentificadorConfigurado_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => InvoicedParty.FinalConsumer("  ", "Consumidor final"));
    }

    /// <summary>
    /// As duas metades têm de bater certo: um engano aqui passaria despercebido
    /// até à exportação.
    /// </summary>
    [Fact]
    public void ConsumidorFinal_ComIdentificadorDeCliente_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, Guid.CreateVersion7(),
                InvoicedParty.FinalConsumer("CONSUMIDORFINAL", "Consumidor final"),
                "AOA", [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    [Fact]
    public void ClienteRegistado_SemIdentificador_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, null, Cliente(), "AOA",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    [Fact]
    public void IdentificadorVazio_NaoEOMesmoQueAusencia()
    {
        // Guid.Empty é engano de quem chama; ausência escreve-se `null`.
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, Guid.Empty, Cliente(), "AOA",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")]));
    }

    // ---- menção de não-validade fiscal ----

    [Fact]
    public void MencaoFiscal_FicaCongeladaNaFactura()
    {
        const string mencao = "Documento sem validade fiscal.";

        var factura = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")], mencao);

        Assert.Equal(mencao, factura.FiscalNotice);
    }

    /// <summary>
    /// É o ponto todo: no dia em que houver certificação, as facturas emitidas
    /// antes continuam a não ser válidas, e a menção tem de continuar nelas.
    /// Derivá-la em leitura apagaria a marca de todo o histórico.
    /// </summary>
    [Fact]
    public void MencaoFiscal_SobreviveAoCancelamento()
    {
        var factura = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")], "Sem validade fiscal.");

        factura.Cancel("Engano", DateTimeOffset.UtcNow);

        Assert.Equal("Sem validade fiscal.", factura.FiscalNotice);
    }

    [Fact]
    public void SemMencaoConfigurada_FicaNula()
    {
        // Nulo é o estado de um sistema certificado. Hoje nenhum ambiente o é,
        // mas o domínio não impõe a menção — quem decide é a configuração.
        Assert.Null(Emitida().FiscalNotice);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void MencaoEmBranco_GuardaSeComoAusente(string mencao)
    {
        var factura = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")], mencao);

        Assert.Null(factura.FiscalNotice);
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var factura = Emitida();
        factura.Cancel("Engano", DateTimeOffset.UtcNow);

        Assert.Equal(0, factura.Version);
    }

    // --- A cadeia de integridade (K7) ---

    [Fact]
    public void FacturaEmitida_TemHashEVerifica()
    {
        var factura = Emitida();

        Assert.NotNull(factura.Hash);
        Assert.True(factura.HashMatches());
    }

    [Fact]
    public void PrimeiraDaSerie_NaoTemEloAnterior()
    {
        Assert.Null(Emitida().PreviousHash);
    }

    [Fact]
    public void FacturasIguaisEmTudoMenosNoElo_TemHashesDiferentes()
    {
        // ⚠ É isto que faz a cadeia ser cadeia. Sem o elo anterior a entrar no
        // cálculo, dois documentos iguais dariam o mesmo hash e reordená-los
        // não se notaria.
        var primeira = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("Consultoria", 1, 100_000m, "NOR", 14m, "ART-TESTE", "UN")],
            previousHash: null);

        var segunda = FacturaDeTeste.Emitir(
            Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
            [new NewInvoiceLine("Consultoria", 1, 100_000m, "NOR", 14m, "ART-TESTE", "UN")],
            previousHash: primeira.Hash);

        Assert.NotEqual(primeira.Hash, segunda.Hash);
    }

    [Fact]
    public void AlterarOTotalDepoisDeEmitida_QuebraOHash()
    {
        // ⚠ O caso que dá sentido a tudo o resto. Sem ele, `HashMatches` podia
        // devolver `true` sempre e todos os outros passariam.
        //
        // A adulteração é por reflexão porque o agregado não a permite — o que
        // é o ponto: quem adultera não passa pelo agregado, mexe na base de
        // dados. É esse ataque que a cadeia deteta.
        var factura = Emitida();

        Assert.True(factura.HashMatches());

        typeof(SalesInvoice)
            .GetProperty(nameof(SalesInvoice.GrossTotal))!
            .SetValue(factura, 1m);

        Assert.False(factura.HashMatches());
    }

    [Fact]
    public void AlterarADataDepoisDeEmitida_QuebraOHash()
    {
        var factura = Emitida();

        typeof(SalesInvoice)
            .GetProperty(nameof(SalesInvoice.IssuedOn))!
            .SetValue(factura, Hoje.AddDays(-30));

        Assert.False(factura.HashMatches());
    }

    [Fact]
    public void FacturaSemHash_NaoVerifica()
    {
        // Uma factura anterior à cadeia. `false` não quer dizer adulterada —
        // quem chama distingue pelo `Hash` nulo, e é por isso que o resumo de
        // `HashMatches` o diz.
        var factura = Emitida();

        typeof(SalesInvoice)
            .GetProperty(nameof(SalesInvoice.Hash))!
            .SetValue(factura, null);

        Assert.False(factura.HashMatches());
        Assert.Null(factura.Hash);
    }

    [Fact]
    public void SemQuemEmitiu_ERecusada()
    {
        // `SourceID` é obrigatório no SAF-T e não se reconstrói: meses depois
        // ninguém sabe quem emitiu a factura.
        Assert.Throws<ArgumentException>(() =>
            FacturaDeTeste.Emitir(
                Numero(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
                [new NewInvoiceLine("X", 1, 10m, "NOR", 14m, "ART-TESTE", "UN")],
                issuedByUserId: Guid.Empty));
    }
}
