using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Contabilidade: plano de contas, partida dobrada e fecho.
///
/// <para>
/// Os códigos usados aqui (<c>61</c>, <c>6111</c>…) são <strong>inventados para
/// o teste</strong> e não pretendem ser o PGC angolano — o Rivo fixa a
/// estrutura, não o conteúdo, e o plano real carrega-se.
/// </para>
/// </summary>
public class LedgerTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static LedgerAccount Raiz(string code = "6") =>
        LedgerAccount.Open(code, "Custos", AccountCategory.GR, null);

    private static LedgerAccount Agregadora(LedgerAccount pai, string code = "61") =>
        LedgerAccount.Open(code, "Fornecimentos e serviços", AccountCategory.GA, pai);

    private static LedgerAccount Movimento(LedgerAccount pai, string code = "6111") =>
        LedgerAccount.Open(code, "Combustíveis", AccountCategory.GM, pai);

    // ---- plano de contas ----

    [Fact]
    public void ContaDePrimeiroGrau_NaoTemAgregadora()
    {
        var raiz = Raiz();

        Assert.True(raiz.IsFirstDegree);
        Assert.Null(raiz.ParentCode);
        Assert.False(raiz.AcceptsPostings);
    }

    [Fact]
    public void ContaDePrimeiroGrau_ComAgregadora_ERecusada()
    {
        var raiz = Raiz();

        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("7", "Proveitos", AccountCategory.GR, raiz));
    }

    /// <summary>
    /// O XSD é explícito: "excepto para as contas do 1.º grau, deve ser
    /// indicada a conta agregadora respectiva".
    /// </summary>
    [Fact]
    public void ContaQueNaoEDePrimeiroGrau_ExigeAgregadora()
    {
        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("61", "Serviços", AccountCategory.GA, null));

        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("6111", "Combustíveis", AccountCategory.GM, null));
    }

    /// <summary>
    /// Uma conta de movimento é folha. Pendurar-lhe filhas faria o saldo dela
    /// deixar de ser o que lá foi lançado.
    /// </summary>
    [Fact]
    public void ContaDeMovimento_NaoAgregaOutras()
    {
        var folha = Movimento(Agregadora(Raiz()));

        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("61111", "Gasóleo", AccountCategory.GM, folha));
    }

    /// <summary>
    /// Geral e analítica são duas árvores. Cruzá-las faria o total da geral
    /// incluir contas analíticas — os mesmos factos contados duas vezes.
    /// </summary>
    [Fact]
    public void ContabilidadeGeralEAnalitica_NaoSeCruzam()
    {
        var geral = Raiz();
        var analitica = LedgerAccount.Open("9", "Analítica", AccountCategory.AR, null);

        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("91", "Centros", AccountCategory.AA, geral));

        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open("61", "Serviços", AccountCategory.GA, analitica));
    }

    [Theory]
    [InlineData("61 11")]
    [InlineData("61#11")]
    [InlineData("")]
    public void CodigoForaDoFormatoDoSafT_ERecusado(string codigo)
    {
        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open(codigo, "Qualquer", AccountCategory.GR, null));
    }

    [Fact]
    public void CodigoAcimaDeTrintaCaracteres_ERecusado()
    {
        Assert.Throws<ArgumentException>(
            () => LedgerAccount.Open(new string('1', 31), "Longa", AccountCategory.GR, null));
    }

    /// <summary>
    /// Só as contas de movimento recebem lançamentos. É a distinção que dá
    /// sentido às seis categorias do SAF-T.
    /// </summary>
    [Theory]
    [InlineData(AccountCategory.GM, true)]
    [InlineData(AccountCategory.AM, true)]
    [InlineData(AccountCategory.GR, false)]
    [InlineData(AccountCategory.GA, false)]
    [InlineData(AccountCategory.AR, false)]
    [InlineData(AccountCategory.AA, false)]
    public void SoContasDeMovimentoAceitamLancamentos(AccountCategory categoria, bool aceita)
    {
        var analitica = categoria is AccountCategory.AA or AccountCategory.AM;

        // O 1.º grau não tem pai; tudo o resto tem, e da mesma contabilidade.
        var pai = categoria is AccountCategory.GR or AccountCategory.AR
            ? null
            : analitica
                ? LedgerAccount.Open("9", "Analítica", AccountCategory.AR, null)
                : Raiz();

        var conta = pai is null
            ? LedgerAccount.Open(analitica ? "9" : "5", "Qualquer", categoria, null)
            : LedgerAccount.Open("5555", "Qualquer", categoria, pai);

        Assert.Equal(aceita, conta.AcceptsPostings);
    }

    /// <summary>
    /// O código é a referência das linhas já lançadas e do que já foi
    /// exportado. Renomear não lhe toca.
    /// </summary>
    [Fact]
    public void RenomearNaoMudaOCodigo()
    {
        var conta = Movimento(Agregadora(Raiz()));
        conta.Rename("Combustíveis e lubrificantes");

        Assert.Equal("6111", conta.Code);
        Assert.Equal("Combustíveis e lubrificantes", conta.Name);
    }

    [Fact]
    public void BootstrapDoPlanoDeContas_CarregaEstruturaBaseEValidaArvore()
    {
        var contas = BootstrapChartOfAccounts.Load().ToList();

        Assert.NotEmpty(contas);
        Assert.Contains(contas, c => c.Code == "1" && c.Category == AccountCategory.GR);
        Assert.Contains(contas, c => c.Code == "10" && c.Category == AccountCategory.GA);
        Assert.Contains(contas, c => c.Code == "1010" && c.Category == AccountCategory.GM);
        Assert.Contains(contas, c => c.Code == "4" && c.Category == AccountCategory.GR);
        Assert.Contains(contas, c => c.Code == "41" && c.Category == AccountCategory.GA);
        Assert.Contains(contas, c => c.Code == "4110" && c.Category == AccountCategory.GM);

        var todasAtivas = contas.Where(c => c.IsActive).ToList();
        Assert.NotEmpty(todasAtivas);
        Assert.True(contas.All(c => !string.IsNullOrWhiteSpace(c.Name)));
        Assert.True(contas.All(c => c.Code.Length <= 30));
    }

    [Fact]
    public void BootstrapDoPlanoDeContas_UsaArvoreCorretaSemContasDeMovimentoAgregadoras()
    {
        var contas = BootstrapChartOfAccounts.Load();
        var contaMovimento = contas.Single(c => c.Code == "1010");
        var contaAgregadora = contas.Single(c => c.Code == "10");

        Assert.True(contaMovimento.AcceptsPostings);
        Assert.False(contaAgregadora.AcceptsPostings);
        Assert.Equal("1", contaAgregadora.ParentCode);
        Assert.Equal("10", contaMovimento.ParentCode);
    }

    [Fact]
    public void VersaoDoPlano_DeveSerVersionadaEValidada()
    {
        var versao = ChartOfAccountsVersion.Create(
            "ANGOLA",
            "PGC",
            "2026-01",
            "Decreto n.º 82/01",
            DateOnly.FromDateTime(DateTime.Today),
            null);

        Assert.Equal("ANGOLA", versao.Jurisdiction);
        Assert.Equal("PGC", versao.Name);
        Assert.Equal("2026-01", versao.Revision);
        Assert.True(versao.IsActive);
    }

    [Fact]
    public void BootstrapDoPlano_DeveGerarVersaoAtivaComContasCarregadas()
    {
        var versao = ChartOfAccountsVersion.BootstrapDevelopment();

        Assert.Equal("ANGOLA", versao.Jurisdiction);
        Assert.Equal("BOOTSTRAP-DEV", versao.Revision);
        Assert.True(versao.IsActive);
        Assert.NotEmpty(versao.Accounts);
        Assert.All(versao.Accounts, conta => Assert.Equal(versao.Id, conta.ChartOfAccountsVersionId));
        Assert.Contains(versao.Accounts, c => c.Code == "1" && c.Category == AccountCategory.GR);
        Assert.Contains(versao.Accounts, c => c.Code == "1010" && c.Category == AccountCategory.GM);
    }

    [Fact]
    public void RegraContabilistica_ExigeEquilibrioEReferenciaLegal()
    {
        var erro = Assert.Throws<ArgumentException>(() =>
            AccountingRule.Create(
                "PURCHASE-GOODS",
                "Compra de mercadorias",
                "Legal",
                "Decreto n.º 82/01",
                DateOnly.FromDateTime(DateTime.Today),
                null,
                [
                    new AccountingRuleLine("1010", EntrySide.Debit, PostingAmount.Net, "Mercadoria"),
                    new AccountingRuleLine("2110", EntrySide.Credit, PostingAmount.Gross, "Fornecedor"),
                ]));

        Assert.Contains("equilibra", erro.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Throws<ArgumentException>(() =>
            AccountingRule.Create(
                "",
                "Compra de mercadorias",
                "Legal",
                "Decreto n.º 82/01",
                DateOnly.FromDateTime(DateTime.Today),
                null,
                [
                    new AccountingRuleLine("1010", EntrySide.Debit, PostingAmount.Net, "Mercadoria"),
                    new AccountingRuleLine("2110", EntrySide.Credit, PostingAmount.Net, "Fornecedor"),
                ]));
    }

    // ---- diários ----

    [Fact]
    public void CodigoDeDiarioComEspaco_ERecusado()
    {
        // O `TransactionID` do SAF-T separa-se por espaços — um espaço no
        // código tornaria a chave impossível de repartir.
        Assert.Throws<ArgumentException>(() => Journal.Open("DI ARIO", "Diversos"));
    }

    // ---- partida dobrada ----

    private static JournalEntry Lancamento(
        decimal debito = 100_000m,
        decimal credito = 100_000m,
        int periodo = 8)
    {
        var raiz = Raiz();
        var agregadora = Agregadora(raiz);
        var custo = Movimento(agregadora, "6111");
        var fornecedor = LedgerAccount.Open("2211", "Fornecedores", AccountCategory.GM, agregadora);

        return JournalEntry.Post(
            Journal.Open("DIV", "Diversos"),
            "ARQ-1",
            Hoje,
            periodo,
            "Combustível de Agosto",
            TransactionType.N,
            "contabilista@rivo.ao",
            [
                new NewJournalLine(custo.Id, custo.Code, EntrySide.Debit, debito, "Custo"),
                new NewJournalLine(fornecedor.Id, fornecedor.Code, EntrySide.Credit, credito, "Dívida"),
            ],
            Agora);
    }

    [Fact]
    public void LancamentoEquilibrado_EAceite()
    {
        var lancamento = Lancamento();

        Assert.Equal(100_000m, lancamento.TotalDebit);
        Assert.Equal(100_000m, lancamento.TotalCredit);
        Assert.Equal(2, lancamento.Lines.Count);
    }

    /// <summary>
    /// <strong>A invariante central da contabilidade.</strong> Sem ela, os
    /// livros deixam de ser livros.
    /// </summary>
    [Fact]
    public void LancamentoQueNaoEquilibra_ERecusado()
    {
        var erro = Assert.Throws<UnbalancedEntryException>(() => Lancamento(100_000m, 90_000m));

        Assert.Contains("equilibra", erro.Message);
    }

    /// <summary>
    /// O XSD exige pelo menos uma linha de cada lado, e a razão não é de
    /// formato: um lançamento só de débitos não diz de onde veio o dinheiro.
    /// </summary>
    [Fact]
    public void LancamentoSoDeUmLado_ERecusado()
    {
        var conta = Movimento(Agregadora(Raiz()));

        Assert.Throws<UnbalancedEntryException>(() => JournalEntry.Post(
            Journal.Open("DIV", "Diversos"), "ARQ-2", Hoje, 8, "Só débitos",
            TransactionType.N, "utilizador",
            [new NewJournalLine(conta.Id, conta.Code, EntrySide.Debit, 100m, "Custo")],
            Agora));
    }

    [Fact]
    public void ValorDeLinhaNaoPositivo_ERecusado()
    {
        var conta = Movimento(Agregadora(Raiz()));

        Assert.Throws<ArgumentOutOfRangeException>(() => JournalEntry.Post(
            Journal.Open("DIV", "Diversos"), "ARQ-3", Hoje, 8, "Zero",
            TransactionType.N, "utilizador",
            [
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Debit, 0m, "Nada"),
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Credit, 0m, "Nada"),
            ],
            Agora));
    }

    /// <summary>
    /// O XSD fixa a composição: data, diário e número de arquivo, separados por
    /// espaços.
    /// </summary>
    [Fact]
    public void TransactionIdSegueACompiscaoDoSafT()
    {
        Assert.Equal("2026-08-25 DIV ARQ-1", Lancamento().TransactionId);
    }

    /// <summary>
    /// 1 a 16, e não 1 a 12: os períodos acima de doze são os de fecho e
    /// regularização, e existem para que o apuramento não se misture com
    /// Dezembro.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(16)]
    public void PeriodoEntreUmEDezasseis_EAceite(int periodo)
    {
        Assert.Equal(periodo, Lancamento(periodo: periodo).Period);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void PeriodoForaDeUmADezasseis_ERecusado(int periodo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lancamento(periodo: periodo));
    }

    [Fact]
    public void NumeroDeArquivoComEspaco_ERecusado()
    {
        var conta = Movimento(Agregadora(Raiz()));

        Assert.Throws<ArgumentException>(() => JournalEntry.Post(
            Journal.Open("DIV", "Diversos"), "ARQ 1", Hoje, 8, "Descrição",
            TransactionType.N, "utilizador",
            [
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Debit, 1m, "A"),
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Credit, 1m, "B"),
            ],
            Agora));
    }

    [Fact]
    public void DiarioDesactivado_NaoRecebeLancamentos()
    {
        var diario = Journal.Open("DIV", "Diversos");
        diario.Deactivate();

        var conta = Movimento(Agregadora(Raiz()));

        Assert.Throws<InvalidOperationException>(() => JournalEntry.Post(
            diario, "ARQ-9", Hoje, 8, "Descrição", TransactionType.N, "utilizador",
            [
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Debit, 1m, "A"),
                new NewJournalLine(conta.Id, conta.Code, EntrySide.Credit, 1m, "B"),
            ],
            Agora));
    }

    [Fact]
    public void AnularExigeMotivoENaoEIdempotente()
    {
        var lancamento = Lancamento();

        Assert.Throws<ArgumentException>(() => lancamento.Void("  ", Agora));

        lancamento.Void("Lançado em duplicado", Agora);

        Assert.True(lancamento.IsVoided);
        Assert.Equal("Lançado em duplicado", lancamento.VoidReason);

        // O segundo motivo apagaria o primeiro sem rasto.
        Assert.Throws<InvalidOperationException>(() => lancamento.Void("Outro", Agora));
    }

    [Fact]
    public void AnularMantemAsLinhas()
    {
        var lancamento = Lancamento();
        lancamento.Void("Engano", Agora);

        Assert.Equal(2, lancamento.Lines.Count);
        Assert.Equal(100_000m, lancamento.TotalDebit);
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDoLancamento()
    {
        var lancamento = Lancamento();
        lancamento.Void("Engano", Agora);

        Assert.Equal(0, lancamento.Version);
    }

    // ---- fecho ----

    [Fact]
    public void PeriodoNasceAbertoEAceitaLancamentos()
    {
        var periodo = AccountingPeriod.Open(2026, 8);

        Assert.Equal(PeriodStatus.Open, periodo.Status);
        Assert.True(periodo.AcceptsPostings);
        Assert.False(periodo.IsAdjustmentPeriod);
    }

    [Fact]
    public void PeriodoAcimaDeDoze_EDeAjustamento()
    {
        Assert.True(AccountingPeriod.Open(2026, 13).IsAdjustmentPeriod);
    }

    [Fact]
    public void FecharParaDeAceitarLancamentos()
    {
        var periodo = AccountingPeriod.Open(2026, 8);
        var quem = Guid.CreateVersion7();

        periodo.Close(quem, Agora);

        Assert.False(periodo.AcceptsPostings);
        Assert.Equal(quem, periodo.ClosedByEmployeeId);
        Assert.Equal(Agora, periodo.ClosedAt);
    }

    [Fact]
    public void FecharDuasVezes_ERecusado()
    {
        var periodo = AccountingPeriod.Open(2026, 8);
        periodo.Close(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(() => periodo.Close(Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void FecharSemQuemFechou_ERecusado()
    {
        Assert.Throws<ArgumentException>(
            () => AccountingPeriod.Open(2026, 8).Close(Guid.Empty, Agora));
    }

    /// <summary>
    /// Reabrir significa que números já dados por definitivos vão mudar. Quem o
    /// faz tem de dizer porquê — e isso vai para a trilha.
    /// </summary>
    [Fact]
    public void ReabrirExigeMotivo()
    {
        var periodo = AccountingPeriod.Open(2026, 8);
        periodo.Close(Guid.CreateVersion7(), Agora);

        Assert.Throws<ArgumentException>(() => periodo.Reopen("   ", Agora));

        periodo.Reopen("Factura de fornecedor chegou depois do fecho", Agora);

        Assert.True(periodo.AcceptsPostings);
        Assert.Equal("Factura de fornecedor chegou depois do fecho", periodo.ReopenReason);
        Assert.Null(periodo.ClosedAt);
    }

    [Fact]
    public void ReabrirUmPeriodoAberto_ERecusado()
    {
        Assert.Throws<InvalidOperationException>(
            () => AccountingPeriod.Open(2026, 8).Reopen("Porquê", Agora));
    }

    /// <summary>
    /// O motivo da reabertura anterior é do ciclo que acabou — mantê-lo faria
    /// parecer que o novo fecho o herdou.
    /// </summary>
    [Fact]
    public void FecharDepoisDeReabrir_LimpaOMotivoAnterior()
    {
        var periodo = AccountingPeriod.Open(2026, 8);
        periodo.Close(Guid.CreateVersion7(), Agora);
        periodo.Reopen("Factura em atraso", Agora);
        periodo.Close(Guid.CreateVersion7(), Agora.AddDays(1));

        Assert.Null(periodo.ReopenReason);
        Assert.Equal(PeriodStatus.Closed, periodo.Status);
    }
}
