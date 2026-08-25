using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// A postagem automática: como um documento vira lançamento.
///
/// <para>
/// <strong>Nada disto cabe num agregado.</strong> A regra é configuração, o
/// plano de contas é outra tabela, o período é outra ainda, e a chave do SAF-T
/// depende do que já foi lançado. É orquestração de ponta a ponta.
/// </para>
/// </summary>
public class PostDocumentTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static (FakeLedgerStore Store, LedgerAccount Cliente, LedgerAccount Proveito, LedgerAccount Imposto) Livros(
        bool comRegra = true,
        bool periodoAberto = true)
    {
        var raizActivo = LedgerAccount.Open("2", "Terceiros", AccountCategory.GR, null);
        var agregadaActivo = LedgerAccount.Open("21", "Clientes", AccountCategory.GA, raizActivo);
        var cliente = LedgerAccount.Open("2111", "Clientes c/c", AccountCategory.GM, agregadaActivo);
        var imposto = LedgerAccount.Open("3431", "IVA liquidado", AccountCategory.GM, agregadaActivo);

        var raizProveito = LedgerAccount.Open("7", "Proveitos", AccountCategory.GR, null);
        var agregadaProveito = LedgerAccount.Open("71", "Vendas", AccountCategory.GA, raizProveito);
        var proveito = LedgerAccount.Open("7111", "Prestação de serviços", AccountCategory.GM, agregadaProveito);

        var periodo = AccountingPeriod.Open(2026, 8);

        if (!periodoAberto)
        {
            periodo.Close(Guid.CreateVersion7(), Agora);
        }

        var store = new FakeLedgerStore()
            .With(raizActivo).With(agregadaActivo).With(cliente).With(imposto)
            .With(raizProveito).With(agregadaProveito).With(proveito)
            .With(Journal.Open("VND", "Vendas"))
            .With(periodo);

        if (comRegra)
        {
            store.With(RegraDeVenda());
        }

        return (store, cliente, proveito, imposto);
    }

    private static PostingRule RegraDeVenda() =>
        PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "VND", "Facturação",
            [
                new NewPostingRuleLine("2111", EntrySide.Debit, PostingAmount.Gross, "Dívida do cliente"),
                new NewPostingRuleLine("7111", EntrySide.Credit, PostingAmount.Net, "Proveito"),
                new NewPostingRuleLine("3431", EntrySide.Credit, PostingAmount.Tax, "IVA liquidado"),
            ]);

    private static DocumentPosting Venda(
        string numero = "FT S001/42",
        decimal liquido = 100_000m,
        decimal imposto = 14_000m,
        DateOnly? data = null) =>
        new(
            PostingEvent.SalesInvoiceIssued,
            numero,
            numero,
            "Venda a Refriango",
            data ?? Hoje,
            liquido,
            imposto,
            liquido + imposto,
            PostingSources.Automatic,
            Agora);

    // ---- o caminho normal ----

    [Fact]
    public async Task DocumentoComRegra_ProduzLancamentoEquilibrado()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);

        var lancamento = await store.FindEntryAsync(resultado.EntryId!.Value, CancellationToken.None);

        Assert.NotNull(lancamento);
        Assert.Equal(114_000m, lancamento.TotalDebit);
        Assert.Equal(114_000m, lancamento.TotalCredit);
        Assert.Equal(3, lancamento.Lines.Count);
    }

    /// <summary>
    /// Cada linha recebe a parcela que a regra lhe atribuiu — e é isso que faz
    /// o proveito ficar sem imposto e o imposto ficar sem proveito.
    /// </summary>
    [Fact]
    public async Task CadaLinhaRecebeASuaParcela()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);
        var lancamento = await store.FindEntryAsync(resultado.EntryId!.Value, CancellationToken.None);

        var divida = lancamento!.Lines.Single(l => l.AccountCode == "2111");
        var proveito = lancamento.Lines.Single(l => l.AccountCode == "7111");
        var iva = lancamento.Lines.Single(l => l.AccountCode == "3431");

        Assert.Equal(114_000m, divida.Amount);
        Assert.Equal(EntrySide.Debit, divida.Side);
        Assert.Equal(100_000m, proveito.Amount);
        Assert.Equal(14_000m, iva.Amount);
    }

    /// <summary>
    /// O número de arquivo deriva do número do documento — e é isso que liga o
    /// lançamento ao papel.
    /// </summary>
    [Fact]
    public async Task NumeroDeArquivoDerivaDoDocumento()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal("2026-08-25 VND FT-S001-42", resultado.TransactionId);
    }

    [Fact]
    public async Task LinhasApontamAoDocumentoDeOrigem()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);
        var lancamento = await store.FindEntryAsync(resultado.EntryId!.Value, CancellationToken.None);

        Assert.All(lancamento!.Lines, l => Assert.Equal("FT S001/42", l.SourceDocumentId));
    }

    /// <summary>
    /// <strong>Idempotência por construção.</strong> Postar o mesmo documento
    /// duas vezes colide na chave única do lançamento em vez de duplicar o
    /// movimento — e a colisão é detectada pelo índice, não por uma verificação
    /// que alguém pode esquecer.
    /// </summary>
    [Fact]
    public async Task MesmoDocumentoDuasVezes_DaAMesmaChave()
    {
        var (store, _, _, _) = Livros();
        var caso = new PostDocument(store);

        var primeiro = await caso.PostAsync(Venda(), CancellationToken.None);
        var segundo = await caso.PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(primeiro.TransactionId, segundo.TransactionId);
    }

    // ---- sem regra ----

    /// <summary>
    /// <strong>O estado por omissão, e tem de ser inofensivo.</strong> O ciclo
    /// de venda funcionou meses sem contabilidade nenhuma; ligar Contabilidade
    /// não pode partir a facturação de quem ainda não carregou um plano.
    /// </summary>
    [Fact]
    public async Task SemRegraConfigurada_NaoPostaENaoFalha()
    {
        var (store, _, _, _) = Livros(comRegra: false);

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.NoRule, resultado.Outcome);
        Assert.Null(resultado.Error);
    }

    [Fact]
    public async Task RegraDeOutroAcontecimento_NaoSeAplica()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(
            Venda() with { Event = PostingEvent.PaymentExecuted }, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.NoRule, resultado.Outcome);
    }

    [Fact]
    public async Task RegraDesactivada_NaoSeAplica()
    {
        var (store, _, _, _) = Livros(comRegra: false);
        var regra = RegraDeVenda();
        regra.Deactivate();
        store.With(regra);

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.NoRule, resultado.Outcome);
    }

    // ---- parcelas nulas ----

    /// <summary>
    /// Uma factura isenta tem imposto zero. A linha do IVA não nasce — o SAF-T
    /// recusa valores não positivos — e <strong>o lançamento continua a
    /// equilibrar</strong>, porque tirar a mesma parcela dos dois lados mantém
    /// a igualdade.
    /// </summary>
    [Fact]
    public async Task ImpostoZero_NaoGeraLinhaEContinuaAEquilibrar()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(
            Venda(liquido: 100_000m, imposto: 0m), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);

        var lancamento = await store.FindEntryAsync(resultado.EntryId!.Value, CancellationToken.None);

        Assert.Equal(2, lancamento!.Lines.Count);
        Assert.DoesNotContain(lancamento.Lines, l => l.AccountCode == "3431");
        Assert.Equal(100_000m, lancamento.TotalDebit);
        Assert.Equal(100_000m, lancamento.TotalCredit);
    }

    // ---- o que trava ----

    /// <summary>
    /// Um documento com data dentro de um período fechado não devia existir. A
    /// postagem trava, e quem chama trava com ela.
    /// </summary>
    [Fact]
    public async Task PeriodoFechado_Trava()
    {
        var (store, _, _, _) = Livros(periodoAberto: false);

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.PeriodClosed, resultado.Outcome);
        Assert.Contains("fechado", resultado.Error);
    }

    /// <summary>
    /// <strong>Um período que ninguém abriu também nunca foi fechado.</strong>
    /// Exigir a linha faria a facturação parar no dia 1 de cada mês.
    /// </summary>
    [Fact]
    public async Task PeriodoQueNinguemAbriu_PostaECriaOPeriodo()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(
            Venda(data: new DateOnly(2026, 9, 1)), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);

        var periodo = await store.FindPeriodAsync(2026, 9, CancellationToken.None);

        Assert.NotNull(periodo);
        Assert.True(periodo.AcceptsPostings);
    }

    /// <summary>
    /// A regra existe e não se consegue honrar. **Trava**: quem a configurou
    /// disse que estes documentos lançam.
    /// </summary>
    [Fact]
    public async Task ContaDaRegraQueDesapareceu_Trava()
    {
        var (store, cliente, _, _) = Livros();
        cliente.Deactivate();

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Failed, resultado.Outcome);
        Assert.Contains("2111", resultado.Error);
    }

    [Fact]
    public async Task DiarioDaRegraDesactivado_Trava()
    {
        var (store, _, _, _) = Livros(comRegra: false);
        var diario = Journal.Open("MORTO", "Morto");
        diario.Deactivate();

        store.With(diario).With(PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "MORTO", "Vendas",
            [
                new NewPostingRuleLine("2111", EntrySide.Debit, PostingAmount.Gross, "Cliente"),
                new NewPostingRuleLine("7111", EntrySide.Credit, PostingAmount.Gross, "Proveito"),
            ]));

        var resultado = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Failed, resultado.Outcome);
    }

    // ---- número de arquivo ----

    /// <summary>
    /// Um documento numerado por terceiros nao pode usar o numero como chave:
    /// dois fornecedores emitem `FT 100` no mesmo dia sem nada de errado.
    /// </summary>
    [Fact]
    public void ChaveDeArquivoDeRegistoNaoDependeDoNumeroDeTerceiros()
    {
        var primeiro = DocumentPosting.KeyFor("FC", Guid.CreateVersion7());
        var segundo = DocumentPosting.KeyFor("FC", Guid.CreateVersion7());

        Assert.NotEqual(primeiro, segundo);
        Assert.StartsWith("FC-", primeiro);

        // Tem de caber nos 20 caracteres do `DocArchivalNumber`.
        Assert.True(primeiro.Length <= 20, $"{primeiro} tem {primeiro.Length} caracteres");
    }

    /// <summary>Determinista: a mesma origem da a mesma chave.</summary>
    [Fact]
    public void ChaveDeArquivoEDeterminista()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(DocumentPosting.KeyFor("PG", id), DocumentPosting.KeyFor("PG", id));
    }

    [Theory]
    [InlineData("FT S001/42", "FT-S001-42")]
    [InlineData("NC S001/7", "NC-S001-7")]
    [InlineData("FT 661054", "FT-661054")]
    public void NumeroDeArquivo_TrocaEspacosEBarras(string documento, string esperado)
    {
        Assert.True(PostDocument.TryArchivalNumber(documento, out var arquivo, out _));
        Assert.Equal(esperado, arquivo);
    }

    /// <summary>
    /// O <c>DocArchivalNumber</c> do SAF-T vai até 20 caracteres. Truncá-lo
    /// deixaria de encontrar o documento no arquivo — e podia colidir com
    /// outro. Recusa-se em vez de encurtar.
    /// </summary>
    [Fact]
    public void NumeroDeArquivoLongoDeMais_ERecusado()
    {
        Assert.False(PostDocument.TryArchivalNumber(
            "FT SERIE-MUITO-LONGA-2026/1", out _, out var erro));

        Assert.Contains("20", erro);
    }

    [Fact]
    public async Task DocumentoComNumeroLongoDeMais_Trava()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new PostDocument(store).PostAsync(
            Venda(numero: "FT SERIE-INTERMINAVEL-2026/1"), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Failed, resultado.Outcome);
    }
}
