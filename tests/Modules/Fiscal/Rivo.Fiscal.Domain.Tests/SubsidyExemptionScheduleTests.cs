using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Domain.Tests;

/// <summary>
/// O limiar de isenção de IRT de um subsídio — a dedução "componentes não
/// sujeitas/isentas" do artigo 7.º do CIRT (`modules/fiscal.md`). Mesmo
/// desenho de <see cref="TaxRateSchedule"/>, com um montante em vez de uma
/// percentagem.
/// </summary>
public class SubsidyExemptionScheduleTests
{
    private static readonly DateOnly Jan2026 = new(2026, 1, 1);
    private static readonly DateOnly Jun2026 = new(2026, 6, 30);
    private static readonly DateOnly Jul2026 = new(2026, 7, 1);

    private static SubsidyExemptionSchedule Alimentacao() => SubsidyExemptionSchedule.Open(SubsidyKind.FoodAllowance);

    [Fact]
    public void VersaoIntroduzida_VigoraNoSeuPeriodo()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");

        Assert.Equal(30_000m, serie.InForceOn(new DateOnly(2026, 3, 15))!.Amount);
    }

    [Fact]
    public void AntesDoInicioDaVigencia_NaoHaLimiar()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");

        // Recusar é a resposta certa — mesma razão de TaxRateSchedule.
        Assert.Null(serie.InForceOn(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void VigenciasSobrepostas_SaoRecusadas()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");

        var erro = Assert.Throws<InvalidOperationException>(() =>
            serie.Introduce(35_000m, Jul2026, null, "Revisão"));

        Assert.Contains("sobrepõe", erro.Message);
    }

    [Fact]
    public void VersaoAnteriorFechada_DeixaEntrarASeguinte()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, Jun2026, "Confirmado pelo utilizador em 2026-08-30");
        serie.Introduce(35_000m, Jul2026, null, "Revisão");

        Assert.Equal(30_000m, serie.InForceOn(new DateOnly(2026, 3, 1))!.Amount);
        Assert.Equal(35_000m, serie.InForceOn(new DateOnly(2026, 9, 1))!.Amount);
    }

    [Fact]
    public void DeterminacaoSegueADataDoFactoGerador_NaoADoCalculo()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, Jun2026, "Confirmado pelo utilizador em 2026-08-30");
        serie.Introduce(35_000m, Jul2026, null, "Revisão");

        var facto = new DateOnly(2026, 2, 10);

        Assert.Equal(30_000m, serie.InForceOn(facto)!.Amount);
    }

    [Fact]
    public void SemInstrumentoLegal_ARecusaEImediata()
    {
        var serie = Alimentacao();

        Assert.Throws<ArgumentException>(() => serie.Introduce(30_000m, Jan2026, null, "   "));
    }

    [Fact]
    public void MontanteNegativo_ERecusado()
    {
        var serie = Alimentacao();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            serie.Introduce(-1m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30"));
    }

    [Fact]
    public void MontanteZero_EAceite()
    {
        // Zero é um limiar válido -- "sem isenção nenhuma", diferente de não
        // ter versão nenhuma (que é "sem resposta").
        var serie = Alimentacao();
        serie.Introduce(0m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");

        Assert.Equal(0m, serie.InForceOn(Jan2026)!.Amount);
    }

    [Fact]
    public void VigenciaQueTerminaAntesDeComecar_ERecusada()
    {
        var serie = Alimentacao();

        Assert.Throws<ArgumentException>(() =>
            serie.Introduce(30_000m, Jul2026, Jan2026, "Confirmado pelo utilizador em 2026-08-30"));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var serie = Alimentacao();
        serie.Introduce(30_000m, Jan2026, Jun2026, "Confirmado pelo utilizador em 2026-08-30");

        // Zero, e é o correcto — ver TaxRateScheduleTests. O incremento é do
        // SaveChangesAsync do DbContext, nunca do domínio.
        Assert.Equal(0, serie.Version);
    }

    [Fact]
    public void AlimentacaoETransporte_SaoSeriesIndependentes()
    {
        var alimentacao = SubsidyExemptionSchedule.Open(SubsidyKind.FoodAllowance);
        var transporte = SubsidyExemptionSchedule.Open(SubsidyKind.TransportAllowance);

        alimentacao.Introduce(30_000m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");
        transporte.Introduce(30_000m, Jan2026, null, "Confirmado pelo utilizador em 2026-08-30");

        Assert.Equal(SubsidyKind.FoodAllowance, alimentacao.Kind);
        Assert.Equal(SubsidyKind.TransportAllowance, transporte.Kind);
        Assert.NotEqual(alimentacao.Id, transporte.Id);
    }
}
