using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// O estorno automático: o lançamento inverso de um documento anulado.
///
/// <para>
/// <strong>Não é `JournalEntry.Void`.</strong> Void diz "isto nunca devia ter
/// sido lançado" e só serve num período aberto; estornar é outro lançamento,
/// datado de hoje, e por isso funciona mesmo quando o período original já
/// fechou. Os dois convivem: o original fica visível (BR-14), e o inverso
/// cancela-o matematicamente.
/// </para>
/// </summary>
public class ReverseDocumentPostingTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static (FakeLedgerStore Store, LedgerAccount Cliente, LedgerAccount Proveito, LedgerAccount Imposto) Livros(
        bool periodoDeHojeAberto = true)
    {
        var raizActivo = LedgerAccount.Open("2", "Terceiros", AccountCategory.GR, null);
        var agregadaActivo = LedgerAccount.Open("21", "Clientes", AccountCategory.GA, raizActivo);
        var cliente = LedgerAccount.Open("2111", "Clientes c/c", AccountCategory.GM, agregadaActivo);
        var imposto = LedgerAccount.Open("3431", "IVA liquidado", AccountCategory.GM, agregadaActivo);

        var raizProveito = LedgerAccount.Open("7", "Proveitos", AccountCategory.GR, null);
        var agregadaProveito = LedgerAccount.Open("71", "Vendas", AccountCategory.GA, raizProveito);
        var proveito = LedgerAccount.Open("7111", "Prestação de serviços", AccountCategory.GM, agregadaProveito);

        var periodo = AccountingPeriod.Open(2026, 8);

        if (!periodoDeHojeAberto)
        {
            periodo.Close(Guid.CreateVersion7(), Agora);
        }

        var store = new FakeLedgerStore()
            .With(raizActivo).With(agregadaActivo).With(cliente).With(imposto)
            .With(raizProveito).With(agregadaProveito).With(proveito)
            .With(Journal.Open("VND", "Vendas"))
            .With(periodo)
            .With(RegraDeVenda());

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

    private static DocumentPosting Venda(string numero = "FT S001/42") =>
        new(
            PostingEvent.SalesInvoiceIssued,
            numero,
            numero,
            "Venda a Refriango",
            Hoje,
            100_000m,
            14_000m,
            114_000m,
            PostingSources.Automatic,
            Agora);

    [Fact]
    public async Task DocumentoLancado_EstornaComAsLinhasTrocadas()
    {
        var (store, _, _, _) = Livros();
        var original = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);

        var estorno = await store.FindEntryAsync(resultado.EntryId!.Value, CancellationToken.None);
        var lancamento = await store.FindEntryAsync(original.EntryId!.Value, CancellationToken.None);

        Assert.NotNull(estorno);
        Assert.Equal(114_000m, estorno.TotalDebit);
        Assert.Equal(114_000m, estorno.TotalCredit);
        Assert.Equal(lancamento!.Lines.Count, estorno.Lines.Count);

        // Mesma conta, mesmo valor, lado trocado -- é isso que faz um estorno,
        // e não uma postagem nova.
        foreach (var linhaOriginal in lancamento.Lines)
        {
            var linhaEstorno = estorno.Lines.Single(l => l.AccountCode == linhaOriginal.AccountCode);

            Assert.Equal(linhaOriginal.Amount, linhaEstorno.Amount);
            Assert.NotEqual(linhaOriginal.Side, linhaEstorno.Side);
        }
    }

    /// <summary>
    /// O original não é tocado — nem anulado, nem eliminado (BR-14). O que o
    /// cancela é a soma dos dois, não a alteração de um deles.
    /// </summary>
    [Fact]
    public async Task OriginalFicaIntacto()
    {
        var (store, _, _, _) = Livros();
        var original = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        var lancamento = await store.FindEntryAsync(original.EntryId!.Value, CancellationToken.None);

        Assert.False(lancamento!.IsVoided);
        Assert.Equal(3, lancamento.Lines.Count);
    }

    [Fact]
    public async Task ChaveDeArquivoDoEstornoEDiferenteDoOriginal()
    {
        var (store, _, _, _) = Livros();
        var original = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        Assert.NotEqual(original.TransactionId, resultado.TransactionId);
    }

    /// <summary>
    /// Sem lançamento original, não há nada a estornar — e isso não é erro. Um
    /// documento emitido antes de haver plano de contas nunca lançou.
    /// </summary>
    [Fact]
    public async Task SemLancamentoOriginal_NaoEstornaENaoFalha()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/999", "Estorno de FT S001/999", Hoje, Agora, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.NoRule, resultado.Outcome);
        Assert.Null(resultado.Error);
    }

    /// <summary>
    /// Um lançamento já anulado manualmente (`VoidJournalEntry`) já não conta
    /// para saldos — estorná-lo também introduziria um desequilíbrio real em
    /// vez de o corrigir.
    /// </summary>
    [Fact]
    public async Task OriginalJaAnulado_NaoEstornaENaoFalha()
    {
        var (store, _, _, _) = Livros();
        var original = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);
        var lancamento = await store.FindEntryForUpdateAsync(original.EntryId!.Value, CancellationToken.None);
        lancamento!.Void("Lançado por engano.", Agora);

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.NoRule, resultado.Outcome);
    }

    /// <summary>
    /// O estorno lança na data de hoje, não na do documento original — e por
    /// isso trava se o período de hoje estiver fechado, não o do original.
    /// </summary>
    [Fact]
    public async Task PeriodoDeHojeFechado_Trava()
    {
        var (store, _, _, _) = Livros();
        await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        var periodo = await store.FindPeriodForUpdateAsync(2026, 8, CancellationToken.None);
        periodo!.Close(Guid.CreateVersion7(), Agora);

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.PeriodClosed, resultado.Outcome);
        Assert.Contains("fechado", resultado.Error);
    }

    /// <summary>
    /// Um período que ninguém abriu também nunca foi fechado -- mesma regra
    /// de <c>PostDocument</c>.
    /// </summary>
    [Fact]
    public async Task PeriodoDeHojeQueNinguemAbriu_EstornaECriaOPeriodo()
    {
        var (store, _, _, _) = Livros();
        await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        var proximoMes = new DateOnly(2026, 9, 15);

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", proximoMes,
            new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);

        var periodo = await store.FindPeriodAsync(2026, 9, CancellationToken.None);
        Assert.NotNull(periodo);
        Assert.True(periodo.AcceptsPostings);
    }

    [Fact]
    public async Task DiarioDoOriginalDesactivado_Trava()
    {
        var (store, _, _, _) = Livros();
        var original = await new PostDocument(store).PostAsync(Venda(), CancellationToken.None);

        var diario = await store.FindJournalByCodeAsync("VND", CancellationToken.None);
        diario!.Deactivate();

        var resultado = await new ReverseDocumentPosting(store).ReverseAsync(
            "FT S001/42", "Estorno de FT S001/42", Hoje, Agora, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Failed, resultado.Outcome);
        Assert.NotNull(original);
    }
}
