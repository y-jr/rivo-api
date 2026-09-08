using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// A travessia da cadeia de integridade (ADR-060).
///
/// <para>
/// <strong>As adulterações são feitas por reflexão, e é de propósito.</strong>
/// O agregado não as permite — quem adultera não passa por ele, mexe na base
/// de dados. É esse ataque que a cadeia existe para detectar, e um teste que o
/// simulasse pelos métodos públicos estaria a testar outra coisa.
/// </para>
/// </summary>
public class VerifyInvoiceChainTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 8);

    private static InvoicedParty Cliente() =>
        new("Kianda Lda", "5417000000", "Rua Rainha Ginga 12", "Luanda", "AO");

    /// <summary>
    /// Emite <paramref name="quantas"/> facturas encadeadas numa série,
    /// fechando o elo a cada uma — como <c>IssueSalesInvoice</c> faz.
    /// </summary>
    private static (DocumentSeries Serie, List<SalesInvoice> Facturas) Serie(int quantas)
    {
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");
        var facturas = new List<SalesInvoice>(quantas);

        for (var i = 0; i < quantas; i++)
        {
            var factura = FacturaDeTeste.Emitir(
                serie.Allocate(),
                Hoje,
                Hoje,
                Guid.CreateVersion7(),
                Cliente(),
                "AOA",
                [new NewInvoiceLine($"Consultoria {i}", 1, 100_000m + i, "NOR", 14m, "ART-TESTE", "UN")],
                previousHash: serie.LastDocumentHash);

            serie.Chain(factura.Hash!);
            facturas.Add(factura);
        }

        return (serie, facturas);
    }

    private static async Task<ChainVerification> Verificar(
        DocumentSeries serie,
        IEnumerable<SalesInvoice> facturas)
    {
        var store = new FakeSalesInvoiceStore().With(serie);

        foreach (var factura in facturas)
        {
            store.With(factura);
        }

        return await new VerifyInvoiceChain(store).ExecuteAsync(CancellationToken.None);
    }

    private static void Adulterar<T>(SalesInvoice factura, string propriedade, T valor) =>
        typeof(SalesInvoice).GetProperty(propriedade)!.SetValue(factura, valor);

    [Fact]
    public async Task CadeiaIntacta_NaoReportaQuebras()
    {
        var (serie, facturas) = Serie(4);

        var resultado = await Verificar(serie, facturas);

        Assert.True(resultado.Intact);

        var relatorio = Assert.Single(resultado.Series);

        Assert.Equal(4, relatorio.Verified);
        Assert.Equal(0, relatorio.BeforeChain);
    }

    [Fact]
    public async Task DocumentoAlterado_EDetectado()
    {
        var (serie, facturas) = Serie(4);

        Adulterar(facturas[1], nameof(SalesInvoice.GrossTotal), 1m);

        var resultado = await Verificar(serie, facturas);

        Assert.False(resultado.Intact);

        var quebra = Assert.Single(resultado.Series.Single().Breaks);

        // ⚠ **Uma quebra, e só uma.** A tentação era esperar um efeito em
        // cascata — a factura seguinte também deixar de fechar. Não acontece,
        // e é melhor assim: o `Hash` gravado da factura 2 não mudou, só deixou
        // de corresponder ao conteúdo dela, por isso a 3 continua a apontar
        // para o elo certo.
        //
        // O relatório nomeia o documento adulterado em vez de acusar toda a
        // série a partir dele. Quem o lê sabe onde ir.
        Assert.Equal(nameof(ChainBreakKind.DocumentAltered), quebra.Kind);
        Assert.Equal(facturas[1].Number.Formatted, quebra.Document);
    }

    [Fact]
    public async Task EloGravadoAlterado_QuebraASequencia()
    {
        // O outro lado do caso acima: aqui mexe-se no `Hash` em vez do
        // conteúdo. A factura 2 passa a não corresponder ao seu elo **e** a 3
        // deixa de apontar para ele — as duas quebras que a outra adulteração
        // não produz.
        var (serie, facturas) = Serie(4);

        Adulterar(facturas[1], nameof(SalesInvoice.Hash), "elo-forjado");

        var quebras = (await Verificar(serie, facturas)).Series.Single().Breaks;

        Assert.Contains(quebras, q =>
            q.Kind == nameof(ChainBreakKind.DocumentAltered) && q.Document == facturas[1].Number.Formatted);

        Assert.Contains(quebras, q =>
            q.Kind == nameof(ChainBreakKind.SequenceBroken) && q.Document == facturas[2].Number.Formatted);
    }

    [Fact]
    public async Task DocumentoRemovidoDoMeio_EDetectado()
    {
        // ⚠ O caso que justifica a travessia existir. Cada factura que sobra
        // continua consistente consigo própria — `HashMatches` devolve `true`
        // em todas. Só a ligação entre elas denuncia a falta.
        var (serie, facturas) = Serie(4);

        Assert.All(facturas, f => Assert.True(f.HashMatches()));

        facturas.RemoveAt(1);

        var resultado = await Verificar(serie, facturas);

        var quebra = Assert.Single(resultado.Series.Single().Breaks);

        Assert.Equal(nameof(ChainBreakKind.SequenceBroken), quebra.Kind);
    }

    [Fact]
    public async Task DocumentosRemovidosDoFim_SaoDetectadosPelaSerie()
    {
        // ⚠ Sem a comparação com `DocumentSeries.LastDocumentHash`, isto
        // passava: as facturas que sobram continuam perfeitamente encadeadas
        // entre si. É o elo guardado na série que sabe que havia mais.
        var (serie, facturas) = Serie(4);

        facturas.RemoveRange(2, 2);

        var resultado = await Verificar(serie, facturas);

        var quebra = Assert.Single(resultado.Series.Single().Breaks);

        Assert.Equal(nameof(ChainBreakKind.SeriesTailMismatch), quebra.Kind);
    }

    [Fact]
    public async Task FacturasAnterioresACadeia_NaoContamComoQuebra()
    {
        // Dizer que documentos legítimos estão adulterados é como uma
        // verificação morre: o relatório passa a ser ignorado.
        var (serie, facturas) = Serie(3);

        Adulterar<string?>(facturas[0], nameof(SalesInvoice.Hash), null);
        Adulterar<string?>(facturas[0], nameof(SalesInvoice.PreviousHash), null);

        var resultado = await Verificar(serie, facturas);

        var relatorio = resultado.Series.Single();

        Assert.Equal(1, relatorio.BeforeChain);
        Assert.Equal(2, relatorio.Verified);

        // A primeira com cadeia passa a ser a segunda factura, e o
        // `PreviousHash` dela aponta para a primeira — que já não tem elo.
        Assert.Contains(relatorio.Breaks, q => q.Kind == nameof(ChainBreakKind.SequenceBroken));
    }

    [Fact]
    public async Task SerieSoComFacturasAnterioresACadeia_EstaIntacta()
    {
        // O estado de uma instalação que actualiza: nada tem elo, a série não
        // tem último elo, e não há nada a acusar.
        var serie = DocumentSeries.Open(DocumentType.FT, "S001");

        var antigas = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var factura = FacturaDeTeste.Emitir(
                    serie.Allocate(), Hoje, Hoje, Guid.CreateVersion7(), Cliente(), "AOA",
                    [new NewInvoiceLine($"Antiga {i}", 1, 1_000m, "NOR", 14m, "ART-TESTE", "UN")]);

                Adulterar<string?>(factura, nameof(SalesInvoice.Hash), null);
                Adulterar<string?>(factura, nameof(SalesInvoice.PreviousHash), null);

                return factura;
            })
            .ToList();

        var resultado = await Verificar(serie, antigas);

        Assert.True(resultado.Intact);
        Assert.Equal(3, resultado.Series.Single().BeforeChain);
    }

    [Fact]
    public async Task SerieVazia_EstaIntacta()
    {
        var resultado = await Verificar(DocumentSeries.Open(DocumentType.FT, "S001"), []);

        Assert.True(resultado.Intact);
    }
}
