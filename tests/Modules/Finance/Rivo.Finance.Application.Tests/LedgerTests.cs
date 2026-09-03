using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Contabilidade &amp; Fecho, na camada que orquestra.
///
/// <para>
/// A partida dobrada é do agregado e tem testes lá. <strong>O que se testa
/// aqui são as três coisas que o agregado não vê:</strong> se o período aceita
/// escrita, se a conta recebe lançamentos, e se a chave do SAF-T já foi usada.
/// Nenhuma delas cabe num lançamento — todas dependem do resto dos livros.
/// </para>
/// </summary>
public class LedgerTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Guid.CreateVersion7(), "10.0.0.1", null);

    private static LedgerAccount Raiz() =>
        LedgerAccount.Open("6", "Custos", AccountCategory.GR, null);

    private static LedgerAccount Agregadora(LedgerAccount raiz) =>
        LedgerAccount.Open("61", "Fornecimentos", AccountCategory.GA, raiz);

    /// <summary>Plano mínimo: raiz, agregadora e duas contas de movimento.</summary>
    private static (FakeLedgerStore Store, LedgerAccount Custo, LedgerAccount Fornecedor, LedgerAccount Agregada) Livros(
        bool periodoAberto = true)
    {
        var raiz = Raiz();
        var agregada = Agregadora(raiz);
        var custo = LedgerAccount.Open("6111", "Combustíveis", AccountCategory.GM, agregada);
        var fornecedor = LedgerAccount.Open("2211", "Fornecedores", AccountCategory.GM, agregada);

        var periodo = AccountingPeriod.Open(2026, 8);

        if (!periodoAberto)
        {
            periodo.Close(Guid.CreateVersion7(), Agora);
        }

        var store = new FakeLedgerStore()
            .With(raiz).With(agregada).With(custo).With(fornecedor)
            .With(Journal.Open("DIV", "Diversos"))
            .With(periodo);

        return (store, custo, fornecedor, agregada);
    }

    private static PostJournalEntry Lancar(FakeLedgerStore store) =>
        new(store, new FakeAuditTrail(), new RelogioFixo(Agora));

    private static List<JournalLineInput> Equilibrado(
        string debito = "6111", string credito = "2211", decimal valor = 100_000m) =>
        [
            new JournalLineInput(debito, EntrySide.Debit, valor, "Custo"),
            new JournalLineInput(credito, EntrySide.Credit, valor, "Dívida"),
        ];

    // ---- o caminho normal ----

    [Fact]
    public async Task LancamentoEquilibradoEmPeriodoAberto_Passa()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Combustível de Agosto", TransactionType.N,
            "contabilista@rivo.ao", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Posted, resultado.Outcome);
        Assert.Equal("2026-08-25 DIV ARQ-1", resultado.TransactionId);
        Assert.Equal(1, store.SaveCount);
    }

    // ---- o período ----

    /// <summary>
    /// <strong>É a recusa que faz de um balancete já entregue um facto</strong>,
    /// em vez de uma vista sobre dados que ainda se mexem.
    /// </summary>
    [Fact]
    public async Task PeriodoFechado_RecusaOLancamento()
    {
        var (store, _, _, _) = Livros(periodoAberto: false);

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Tardio", TransactionType.N,
            "contabilista@rivo.ao", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.PeriodClosed, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>
    /// <strong>Um período que ninguém abriu também nunca foi fechado.</strong>
    ///
    /// <para>
    /// A linha existe para registar um fecho, não para dar licença. Exigi-la
    /// faria a facturação parar no dia 1 de cada mês por arrumação
    /// contabilística por fazer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PeriodoQueNinguemAbriu_AceitaLancamentos()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", new DateOnly(2027, 3, 1), 2027, 3, "Ano seguinte",
            TransactionType.N, "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Posted, resultado.Outcome);

        // A linha do período passa a existir, aberta: regista um *fecho*, e
        // ninguém fechou nada.
        var periodo = await store.FindPeriodAsync(2027, 3, CancellationToken.None);

        Assert.NotNull(periodo);
        Assert.True(periodo.AcceptsPostings);
    }

    /// <summary>
    /// O período é a primeira verificação. Um lançamento que nem sequer
    /// equilibra, num período fechado, deve ouvir falar do período — é o que
    /// impede a escrita, e o outro erro só apareceria depois de o corrigir.
    /// </summary>
    [Fact]
    public async Task PeriodoFechado_ERelatadoAntesDeOutrosDefeitos()
    {
        var (store, _, _, _) = Livros(periodoAberto: false);

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Desequilibrado e tardio", TransactionType.N,
            "utilizador",
            [
                new JournalLineInput("6111", EntrySide.Debit, 100m, "Custo"),
                new JournalLineInput("2211", EntrySide.Credit, 90m, "Dívida"),
            ],
            Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.PeriodClosed, resultado.Outcome);
    }

    // ---- as contas ----

    /// <summary>
    /// Lançar numa agregadora faria o total dela deixar de ser a soma das
    /// filhas — o erro clássico que um plano hierárquico existe para impedir.
    /// </summary>
    [Fact]
    public async Task LancarEmContaAgregadora_ERecusado()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Na agregadora", TransactionType.N, "utilizador",
            Equilibrado(debito: "61"), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Rejected, resultado.Outcome);
        Assert.Contains("agregadora", resultado.Error);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ContaInexistente_ERecusada()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Conta que não existe", TransactionType.N,
            "utilizador", Equilibrado(debito: "9999"), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.AccountNotFound, resultado.Outcome);
    }

    [Fact]
    public async Task ContaDesactivada_ERecusada()
    {
        var (store, custo, _, _) = Livros();
        custo.Deactivate();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Conta fechada", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Rejected, resultado.Outcome);
        Assert.Contains("desactivada", resultado.Error);
    }

    [Fact]
    public async Task DiarioInexistente_ERecusado()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "SEM-DIARIO", "ARQ-1", Hoje, 2026, 8, "Descrição", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.JournalNotFound, resultado.Outcome);
    }

    // ---- a chave do SAF-T ----

    /// <summary>
    /// O <c>TransactionID</c> é composto por três coisas que quem lança escolhe.
    /// Nada impede repeti-las por engano — e o ficheiro só seria recusado meses
    /// depois.
    /// </summary>
    [Fact]
    public async Task ChaveDoSafTRepetida_ERecusada()
    {
        var (store, _, _, _) = Livros();
        var caso = Lancar(store);

        var primeiro = await caso.ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Primeiro", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        var segundo = await caso.ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Segundo, mesma chave", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Posted, primeiro.Outcome);
        Assert.Equal(PostEntryOutcome.DuplicateTransaction, segundo.Outcome);
    }

    /// <summary>
    /// O mesmo número de arquivo noutro dia é outra chave — e é legítimo.
    /// </summary>
    [Fact]
    public async Task MesmoArquivoNoutraData_EOutraChave()
    {
        var (store, _, _, _) = Livros();
        store.With(AccountingPeriod.Open(2026, 7));

        var caso = Lancar(store);

        await caso.ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Agosto", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        var outro = await caso.ExecuteAsync(
            "DIV", "ARQ-1", new DateOnly(2026, 7, 25), 2026, 7, "Julho", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Posted, outro.Outcome);
    }

    // ---- partida dobrada, com resultado próprio ----

    /// <summary>
    /// Não basta recusar: a razão tem de ser distinguível. Um lançamento
    /// desequilibrado não é "pedido inválido" — é a invariante central da
    /// contabilidade a ser violada.
    /// </summary>
    [Fact]
    public async Task LancamentoDesequilibrado_TemResultadoProprio()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Não bate", TransactionType.N, "utilizador",
            [
                new JournalLineInput("6111", EntrySide.Debit, 100_000m, "Custo"),
                new JournalLineInput("2211", EntrySide.Credit, 90_000m, "Dívida"),
            ],
            Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Unbalanced, resultado.Outcome);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task LancamentoSemLinhas_ERecusado()
    {
        var (store, _, _, _) = Livros();

        var resultado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Vazio", TransactionType.N, "utilizador",
            [], Contexto, CancellationToken.None);

        Assert.Equal(PostEntryOutcome.Rejected, resultado.Outcome);
    }

    // ---- anulação ----

    /// <summary>
    /// Anular num período fechado mudaria um balancete já reportado sem deixar
    /// rasto no próprio período. Para isso existe o lançamento de
    /// regularização, que é visível.
    /// </summary>
    [Fact]
    public async Task AnularEmPeriodoFechado_ERecusado()
    {
        var (store, _, _, _) = Livros();
        var caso = Lancar(store);

        var lancado = await caso.ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Normal", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        // Fecha-se o período depois de lançar.
        var periodo = await store.FindPeriodForUpdateAsync(2026, 8, CancellationToken.None);
        periodo!.Close(Guid.CreateVersion7(), Agora);

        var anulacao = await new VoidJournalEntry(store, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(lancado.EntryId!.Value, "Engano", Contexto, CancellationToken.None);

        Assert.Equal(VoidEntryOutcome.PeriodClosed, anulacao.Outcome);
        Assert.Contains("regularização", anulacao.Error);
    }

    [Fact]
    public async Task AnularEmPeriodoAberto_Passa()
    {
        var (store, _, _, _) = Livros();

        var lancado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Normal", TransactionType.N,
            "utilizador", Equilibrado(), Contexto, CancellationToken.None);

        var anulacao = await new VoidJournalEntry(store, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(lancado.EntryId!.Value, "Lançado em duplicado", Contexto, CancellationToken.None);

        Assert.Equal(VoidEntryOutcome.Voided, anulacao.Outcome);
    }

    // ---- balancete ----

    [Fact]
    public async Task BalanceteSomaPorContaEEquilibra()
    {
        var (store, _, _, _) = Livros();

        await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "Combustível", TransactionType.N,
            "utilizador", Equilibrado(valor: 100_000m), Contexto, CancellationToken.None);

        var balancete = await new GetTrialBalance(store)
            .ExecuteAsync(2026, null, CancellationToken.None);

        Assert.True(balancete.IsBalanced);
        Assert.Equal(100_000m, balancete.TotalDebit);
        Assert.Equal(100_000m, balancete.TotalCredit);
        Assert.Equal(2, balancete.Lines.Count);

        var custo = balancete.Lines.Single(l => l.AccountCode == "6111");
        Assert.Equal(100_000m, custo.ClosingDebit);
        Assert.Equal(0m, custo.ClosingCredit);
    }

    /// <summary>
    /// Um balancete que somasse lançamentos anulados mostraria dinheiro que a
    /// anulação retirou.
    /// </summary>
    [Fact]
    public async Task BalanceteIgnoraLancamentosAnulados()
    {
        var (store, _, _, _) = Livros();

        var lancado = await Lancar(store).ExecuteAsync(
            "DIV", "ARQ-1", Hoje, 2026, 8, "A anular", TransactionType.N,
            "utilizador", Equilibrado(valor: 100_000m), Contexto, CancellationToken.None);

        await new VoidJournalEntry(store, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(lancado.EntryId!.Value, "Engano", Contexto, CancellationToken.None);

        var balancete = await new GetTrialBalance(store)
            .ExecuteAsync(2026, null, CancellationToken.None);

        Assert.Equal(0m, balancete.TotalDebit);
        Assert.Empty(balancete.Lines);
    }

    /// <summary>
    /// <strong>A abertura de um período é o fecho do anterior.</strong> Não é
    /// número guardado à parte, e por isso não pode divergir.
    /// </summary>
    [Fact]
    public async Task AberturaDeUmPeriodoEOFechoDoAnterior()
    {
        var (store, _, _, _) = Livros();
        store.With(AccountingPeriod.Open(2026, 7));

        var caso = Lancar(store);

        await caso.ExecuteAsync(
            "DIV", "JUL-1", new DateOnly(2026, 7, 10), 2026, 7, "Julho", TransactionType.N,
            "utilizador", Equilibrado(valor: 60_000m), Contexto, CancellationToken.None);

        await caso.ExecuteAsync(
            "DIV", "AGO-1", Hoje, 2026, 8, "Agosto", TransactionType.N,
            "utilizador", Equilibrado(valor: 40_000m), Contexto, CancellationToken.None);

        var agosto = await new GetTrialBalance(store).ExecuteAsync(2026, 8, CancellationToken.None);
        var custo = agosto.Lines.Single(l => l.AccountCode == "6111");

        Assert.Equal(60_000m, custo.OpeningDebit);
        Assert.Equal(40_000m, custo.PeriodDebit);
        Assert.Equal(100_000m, custo.ClosingDebit);
    }

    // ---- plano de contas ----

    [Fact]
    public async Task CodigoDeContaRepetido_ERecusado()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new OpenLedgerAccount(store, new FakeAuditTrail()).ExecuteAsync(
            "6111", "Outro combustível", AccountCategory.GM, "61",
            Contexto, CancellationToken.None);

        Assert.Equal(OpenLedgerAccountOutcome.Duplicate, resultado.Outcome);
    }

    /// <summary>
    /// O plano carrega-se de cima para baixo. Uma conta cuja agregadora ainda
    /// não existe é ordem errada, não erro de dados.
    /// </summary>
    [Fact]
    public async Task AgregadoraInexistente_ERecusada()
    {
        var (store, _, _, _) = Livros();

        var resultado = await new OpenLedgerAccount(store, new FakeAuditTrail()).ExecuteAsync(
            "7111", "Vendas", AccountCategory.GM, "71", Contexto, CancellationToken.None);

        Assert.Equal(OpenLedgerAccountOutcome.ParentNotFound, resultado.Outcome);
    }

    /// <summary>
    /// Uma agregadora com filhas activas não se desactiva: a árvore ficaria com
    /// um buraco no meio.
    /// </summary>
    [Fact]
    public async Task DesactivarAgregadoraComFilhas_ERecusado()
    {
        var (store, _, _, agregada) = Livros();

        var outcome = await new DeactivateLedgerAccount(store, new FakeAuditTrail())
            .ExecuteAsync(agregada.Id, Contexto, CancellationToken.None);

        Assert.Equal(DeactivateAccountOutcome.HasChildren, outcome);
        Assert.True(agregada.IsActive);
    }

    [Fact]
    public async Task DesactivarFolha_Passa()
    {
        var (store, custo, _, _) = Livros();

        var outcome = await new DeactivateLedgerAccount(store, new FakeAuditTrail())
            .ExecuteAsync(custo.Id, Contexto, CancellationToken.None);

        Assert.Equal(DeactivateAccountOutcome.Done, outcome);
        Assert.False(custo.IsActive);
    }

    // ---- Versões do plano de contas ----

    [Fact]
    public async Task CriarVersaoDoPlano_Passa()
    {
        var store = new FakeLedgerStore();

        var resultado = await new CreateChartOfAccountsVersion(store, new FakeAuditTrail()).ExecuteAsync(
            "ANGOLA",
            "PGC",
            "2024",
            "Plano Geral de Contas vigente",
            new DateOnly(2024, 1, 1),
            null,
            Contexto,
            CancellationToken.None);

        Assert.Equal(CreateChartOfAccountsVersionOutcome.Created, resultado.Outcome);
        Assert.NotNull(resultado.ChartVersionId);
    }

    [Fact]
    public async Task VersaoRepetida_ERecusada()
    {
        var store = new FakeLedgerStore();
        var versao = ChartOfAccountsVersion.BootstrapDevelopment();
        store.With(versao);

        var resultado = await new CreateChartOfAccountsVersion(store, new FakeAuditTrail()).ExecuteAsync(
            versao.Jurisdiction,
            versao.Name,
            versao.Revision,
            versao.Source,
            versao.EffectiveFrom,
            versao.EffectiveTo,
            Contexto,
            CancellationToken.None);

        Assert.Equal(CreateChartOfAccountsVersionOutcome.Duplicate, resultado.Outcome);
    }

    [Fact]
    public async Task ListarVersoesDoPlano_RetornaOsMetadados()
    {
        var store = new FakeLedgerStore();
        var v1 = ChartOfAccountsVersion.Create(
            "ANGOLA", "PGC", "2024", "Oficial 2024",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var v2 = ChartOfAccountsVersion.BootstrapDevelopment();
        store.With(v1).With(v2);

        var resultado = await new ListChartOfAccountsVersions(store).ExecuteAsync(
            false, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.Single(resultado, v => v.ChartVersionId == v1.Id);
        Assert.Single(resultado, v => v.ChartVersionId == v2.Id);
    }

    // ---- Regras contabilísticas ----

    [Fact]
    public async Task CriarRegraContabilistica_Passa()
    {
        var (store, custo, fornecedor, _) = Livros();
        var versao = ChartOfAccountsVersion.BootstrapDevelopment();
        store.With(versao);

        var resultado = await new CreateAccountingRule(store, new FakeAuditTrail()).ExecuteAsync(
            "FIN-01",
            "Factura de Fornecedor",
            "BusinessEvent",
            "Regra padrão para facturas de fornecedor",
            new DateOnly(2026, 1, 1),
            null,
            [
                new AccountingRuleLineInput("6111", EntrySide.Debit, PostingAmount.Gross, "Custo Total"),
                new AccountingRuleLineInput("2211", EntrySide.Credit, PostingAmount.Gross, "Dívida Total"),
            ],
            Contexto,
            CancellationToken.None);

        Assert.Equal(CreateAccountingRuleOutcome.Created, resultado.Outcome);
        Assert.NotNull(resultado.RuleId);
    }

    [Fact]
    public async Task RegraComContaInexistente_ERecusada()
    {
        var store = new FakeLedgerStore();
        var versao = ChartOfAccountsVersion.BootstrapDevelopment();
        store.With(versao);

        var resultado = await new CreateAccountingRule(store, new FakeAuditTrail()).ExecuteAsync(
            "BAD-01",
            "Regra Inválida",
            "BusinessEvent",
            "Testa validação de conta",
            new DateOnly(2026, 1, 1),
            null,
            [
                new AccountingRuleLineInput("9999", EntrySide.Debit, PostingAmount.Gross, "Conta fantasma"),
                new AccountingRuleLineInput("2211", EntrySide.Credit, PostingAmount.Gross, "Crédito"),
            ],
            Contexto,
            CancellationToken.None);

        Assert.Equal(CreateAccountingRuleOutcome.AccountNotFound, resultado.Outcome);
        Assert.Contains("não existe", resultado.Error);
    }

    [Fact]
    public async Task ListarRegrasContabilisticas_RetornaAsLinhas()
    {
        var store = new FakeLedgerStore();
        var versao = ChartOfAccountsVersion.BootstrapDevelopment();
        store.With(versao);

        var regra = AccountingRule.Create(
            "FIN-01",
            "Factura de Fornecedor",
            "BusinessEvent",
            "Padrão para facturas",
            new DateOnly(2026, 1, 1),
            null,
            [
                new AccountingRuleLine("1010", EntrySide.Debit, PostingAmount.Gross, "Mercadoria Total"),
                new AccountingRuleLine("2110", EntrySide.Credit, PostingAmount.Gross, "Fornecedor Total"),
            ]);

        await store.AddAccountingRuleAsync(regra, CancellationToken.None);

        var resultado = await new ListAccountingRules(store).ExecuteAsync(
            false, CancellationToken.None);

        Assert.Single(resultado);
        var view = resultado.First();
        Assert.Equal("FIN-01", view.Code);
        Assert.Equal(2, view.Lines.Count);
    }

    // ---- Integração com PlanoDeContas ----

    [Fact]
    public async Task PostDocument_ComPlanoCorrecto_Passa()
    {
        var (store, custo, fornecedor, _) = Livros();
        
        // Criar versão do plano que contém as contas
        var versao = ChartOfAccountsVersion.Create(
            "ANGOLA", "PGC", "2026", "Oficial",
            new DateOnly(2026, 1, 1), null);
        versao.AddAccounts([custo, fornecedor]);
        store.With(versao);

        // Regra de postagem que usa essas contas
        var regra = PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "DIV", "Vendas",
            [
                new NewPostingRuleLine("6111", EntrySide.Debit, PostingAmount.Net, "Custo"),
                new NewPostingRuleLine("2211", EntrySide.Credit, PostingAmount.Net, "Fornecedor"),
            ]);
        store.With(regra);

        var posting = new DocumentPosting(
            PostingEvent.SalesInvoiceIssued,
            "FT S001/1",
            "FT-S001-1",
            "Factura de Teste",
            Hoje,
            Net: 100_000m,
            Tax: 20_000m,
            Gross: 120_000m,
            SourceId: Guid.CreateVersion7().ToString(),
            At: Agora);

        var resultado = await new PostDocument(store).PostAsync(posting, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);
    }

    [Fact]
    public async Task PostDocument_ComPlanoQueNaoTemConta_Falha()
    {
        var (store, custo, fornecedor, _) = Livros();
        
        // Versão do plano que não contém a conta 2211
        var versao = ChartOfAccountsVersion.Create(
            "ANGOLA", "PGC", "2026", "Oficial",
            new DateOnly(2026, 1, 1), null);
        versao.AddAccounts([custo]); // Só adicionamos custo, não fornecedor
        store.With(versao);

        // Regra que usa custo e fornecedor
        var regra = PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "DIV", "Vendas",
            [
                new NewPostingRuleLine("6111", EntrySide.Debit, PostingAmount.Net, "Custo"),
                new NewPostingRuleLine("2211", EntrySide.Credit, PostingAmount.Net, "Fornecedor"),
            ]);
        store.With(regra);

        var posting = new DocumentPosting(
            PostingEvent.SalesInvoiceIssued,
            "FT S001/1",
            "FT-S001-1",
            "Factura de Teste",
            Hoje,
            Net: 100_000m,
            Tax: 20_000m,
            Gross: 120_000m,
            SourceId: Guid.CreateVersion7().ToString(),
            At: Agora);

        var resultado = await new PostDocument(store).PostAsync(posting, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Failed, resultado.Outcome);
        Assert.Contains("não existe no plano", resultado.Error);
    }

    [Fact]
    public async Task PostDocument_SemPlano_Passa()
    {
        var (store, custo, fornecedor, _) = Livros();
        
        // Sem versão do plano definida — backwards compatible

        // Regra que usa essas contas
        var regra = PostingRule.Define(
            PostingEvent.SalesInvoiceIssued, "DIV", "Vendas",
            [
                new NewPostingRuleLine("6111", EntrySide.Debit, PostingAmount.Net, "Custo"),
                new NewPostingRuleLine("2211", EntrySide.Credit, PostingAmount.Net, "Fornecedor"),
            ]);
        store.With(regra);

        var posting = new DocumentPosting(
            PostingEvent.SalesInvoiceIssued,
            "FT S001/1",
            "FT-S001-1",
            "Factura de Teste",
            Hoje,
            Net: 100_000m,
            Tax: 20_000m,
            Gross: 120_000m,
            SourceId: Guid.CreateVersion7().ToString(),
            At: Agora);

        var resultado = await new PostDocument(store).PostAsync(posting, CancellationToken.None);

        Assert.Equal(DocumentPostingOutcome.Posted, resultado.Outcome);
    }
}
