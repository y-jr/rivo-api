using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Domain.Tests;

/// <summary>
/// A tabela de escalões de IRT — Tabela B (`docs/rivo-fiscal-regras-angola-v1.md`
/// §1.4), com os dois valores que estavam por confirmar já resolvidos
/// directamente pelo utilizador (não por levantamento secundário): a parcela
/// fixa de 12.500 Kz no escalão 2, e 292.250 Kz no escalão 7.
/// </summary>
public class IncomeTaxScheduleTests
{
    private static readonly DateOnly Jan2026 = new(2026, 1, 1);
    private static readonly DateOnly Jun2026 = new(2026, 6, 30);
    private static readonly DateOnly Jul2026 = new(2026, 7, 1);

    /// <summary>Os 11 escalões da Tabela B, tal como confirmados.</summary>
    private static readonly NewIncomeTaxBracket[] TabelaB =
    [
        new(0m, 0m, 0m),
        new(150_000m, 12_500m, 16.0m),
        new(200_000m, 31_250m, 18.0m),
        new(300_000m, 49_250m, 19.0m),
        new(500_000m, 87_250m, 20.0m),
        new(1_000_000m, 187_250m, 21.0m),
        new(1_500_000m, 292_250m, 22.0m),
        new(2_000_000m, 402_250m, 23.0m),
        new(2_500_000m, 517_250m, 24.0m),
        new(5_000_000m, 1_117_250m, 24.5m),
        new(10_000_000m, 2_342_250m, 25.0m),
    ];

    private static IncomeTaxSchedule ComTabelaB()
    {
        var tabela = IncomeTaxSchedule.Open();
        tabela.Introduce(TabelaB, Jan2026, null, "Lei n.º 14/25");
        return tabela;
    }

    [Fact]
    public void VersaoIntroduzida_VigoraNoSeuPeriodo()
    {
        var tabela = ComTabelaB();

        Assert.NotNull(tabela.InForceOn(new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public void AntesDoInicioDaVigencia_NaoHaTabela()
    {
        var tabela = ComTabelaB();

        // Recusar é a resposta certa — ver TaxRateScheduleTests.
        Assert.Null(tabela.InForceOn(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void VigenciasSobrepostas_SaoRecusadas()
    {
        var tabela = ComTabelaB();

        var erro = Assert.Throws<InvalidOperationException>(() =>
            tabela.Introduce(TabelaB, Jul2026, null, "Lei n.º 20/26"));

        Assert.Contains("sobrepõe", erro.Message);
    }

    [Fact]
    public void VersaoAnteriorFechada_DeixaEntrarASeguinte()
    {
        var tabela = IncomeTaxSchedule.Open();
        tabela.Introduce(TabelaB, Jan2026, Jun2026, "Lei n.º 14/25");
        tabela.Introduce(TabelaB, Jul2026, null, "Lei n.º 20/26");

        Assert.NotNull(tabela.InForceOn(new DateOnly(2026, 3, 1)));
        Assert.NotNull(tabela.InForceOn(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void SemInstrumentoLegal_ARecusaEImediata()
    {
        var tabela = IncomeTaxSchedule.Open();

        Assert.Throws<ArgumentException>(() =>
            tabela.Introduce(TabelaB, Jan2026, null, "   "));
    }

    [Fact]
    public void VigenciaQueTerminaAntesDeComecar_ERecusada()
    {
        var tabela = IncomeTaxSchedule.Open();

        Assert.Throws<ArgumentException>(() =>
            tabela.Introduce(TabelaB, Jul2026, Jan2026, "Lei n.º 14/25"));
    }

    [Fact]
    public void SemEscaloes_ERecusada()
    {
        var tabela = IncomeTaxSchedule.Open();

        Assert.Throws<ArgumentException>(() =>
            tabela.Introduce([], Jan2026, null, "Lei n.º 14/25"));
    }

    [Fact]
    public void PrimeiroEscalaoTemDeComecarEmZero()
    {
        var tabela = IncomeTaxSchedule.Open();

        var erro = Assert.Throws<ArgumentException>(() =>
            tabela.Introduce([new NewIncomeTaxBracket(1m, 0m, 16m)], Jan2026, null, "Lei n.º 14/25"));

        Assert.Contains("zero", erro.Message);
    }

    [Fact]
    public void EscaloesComOMesmoLimiar_SaoRecusados()
    {
        var tabela = IncomeTaxSchedule.Open();

        var duplicados = new NewIncomeTaxBracket[]
        {
            new(0m, 0m, 0m),
            new(150_000m, 12_500m, 16m),
            new(150_000m, 20_000m, 17m),
        };

        var erro = Assert.Throws<ArgumentException>(() =>
            tabela.Introduce(duplicados, Jan2026, null, "Lei n.º 14/25"));

        Assert.Contains("ambígua", erro.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void TaxaForaDeZeroACem_ERecusada(decimal taxa)
    {
        var tabela = IncomeTaxSchedule.Open();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tabela.Introduce([new NewIncomeTaxBracket(0m, 0m, taxa)], Jan2026, null, "Lei n.º 14/25"));
    }

    [Fact]
    public void ParcelaFixaNegativa_ERecusada()
    {
        var tabela = IncomeTaxSchedule.Open();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tabela.Introduce([new NewIncomeTaxBracket(0m, -1m, 0m)], Jan2026, null, "Lei n.º 14/25"));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var tabela = ComTabelaB();

        // Zero, e é o correcto — ver TaxRateScheduleTests. O incremento é do
        // SaveChangesAsync do DbContext, nunca do domínio.
        Assert.Equal(0, tabela.Version);
    }

    // --- Selecção de escalão: fronteiras exactas (Tabela B) ---------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(150_000, 0)]
    public void RendimentoNaOuAbaixoDoLimiteDeIsencao_CaiNoEscalaoDeIsencao(decimal rendimento, decimal irtEsperado)
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        Assert.Equal(irtEsperado, versao.Compute(rendimento));
    }

    [Fact]
    public void UmKwanzaAcimaDoLimiteDeIsencao_JaPagaImposto()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        // 150.001: primeiro Kz do escalão 2 — a parcela fixa sozinha, porque
        // (150.001 − 150.000) × 16% arredonda a uma fracção desprezável.
        var irt = versao.Compute(150_001m);

        Assert.Equal(12_500.16m, irt);
    }

    [Fact]
    public void ExactamenteNoTopoDoEscalao2_AindaPertenceAoEscalao2()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        // 200.000: ainda "excesso de 150.000", não "excesso de 200.000" — o
        // limiar em si pertence ao escalão anterior (SelectBracket usa `>`).
        var irt = versao.Compute(200_000m);

        Assert.Equal(12_500m + (200_000m - 150_000m) * 0.16m, irt);
    }

    [Fact]
    public void UmKwanzaAcimaDoTopoDoEscalao2_JaCaiNoEscalao3()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        var irt = versao.Compute(200_001m);

        Assert.Equal(31_250m + (200_001m - 200_000m) * 0.18m, irt);
    }

    [Fact]
    public void UltimoEscalao_NaoTemTecto()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        var irt = versao.Compute(50_000_000m);

        Assert.Equal(2_342_250m + (50_000_000m - 10_000_000m) * 0.25m, irt);
    }

    [Fact]
    public void MateriaColectavelNegativa_ERecusada()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        Assert.Throws<ArgumentOutOfRangeException>(() => versao.Compute(-1m));
    }

    /// <summary>
    /// O exemplo integral de `docs/rivo-fiscal-regras-angola-v1.md` §1.6 —
    /// bruto 250.000, INSS do trabalhador a 3%, IRT sobre a matéria
    /// colectável já líquida de INSS. Fixado como regressão: se este número
    /// mudar sem ninguém ter mexido na tabela, algo no motor quebrou.
    /// </summary>
    [Fact]
    public void ExemploDocumentado_Bruto250000_IRT38900()
    {
        var versao = ComTabelaB().InForceOn(Jan2026)!;

        const decimal bruto = 250_000m;
        const decimal inssTrabalhador = 7_500m; // 3% de 250.000
        const decimal materiaColectavel = bruto - inssTrabalhador; // 242.500

        var irt = versao.Compute(materiaColectavel);

        Assert.Equal(38_900m, irt);
    }
}
