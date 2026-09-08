using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Domain.Tests;

/// <summary>
/// A série de taxas é o que torna o ADR-011 executável: taxas como dados com
/// vigência, determinação à data do facto gerador.
/// </summary>
public class TaxRateScheduleTests
{
    private static readonly DateOnly Jan2026 = new(2026, 1, 1);
    private static readonly DateOnly Jun2026 = new(2026, 6, 30);
    private static readonly DateOnly Jul2026 = new(2026, 7, 1);

    private static TaxRateSchedule Normal() =>
        TaxRateSchedule.Open(TaxKind.ValueAdded, "NOR", "IVA — taxa normal");

    [Fact]
    public void VersaoIntroduzida_VigoraNoSeuPeriodo()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, null, "Lei n.º 14/23");

        Assert.Equal(14m, serie.InForceOn(new DateOnly(2026, 3, 15))!.Percentage);
    }

    [Fact]
    public void AntesDoInicioDaVigencia_NaoHaTaxa()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, null, "Lei n.º 14/23");

        // Recusar é a resposta certa: recair na versão mais próxima inventaria
        // o valor de um documento que não está coberto.
        Assert.Null(serie.InForceOn(new DateOnly(2025, 12, 31)));
    }

    /// <summary>
    /// A invariante que faz a determinação ser determinística. Sem ela,
    /// "que taxa vigorava em Março" pode ter duas respostas.
    /// </summary>
    [Fact]
    public void VigenciasSobrepostas_SaoRecusadas()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, null, "Lei n.º 14/23");

        var erro = Assert.Throws<InvalidOperationException>(() =>
            serie.Introduce(17m, Jul2026, null, "Lei n.º 20/26"));

        Assert.Contains("sobrepõe", erro.Message);
    }

    [Fact]
    public void VersaoAnteriorFechada_DeixaEntrarASeguinte()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, Jun2026, "Lei n.º 14/23");
        serie.Introduce(17m, Jul2026, null, "Lei n.º 20/26");

        Assert.Equal(14m, serie.InForceOn(new DateOnly(2026, 3, 1))!.Percentage);
        Assert.Equal(17m, serie.InForceOn(new DateOnly(2026, 9, 1))!.Percentage);
    }

    /// <summary>
    /// ADR-011 §3. Uma correcção emitida hoje sobre um facto do ano passado
    /// aplica as regras do ano passado — é o comportamento inteiro do módulo
    /// numa asserção.
    /// </summary>
    [Fact]
    public void DeterminacaoSegueADataDoFactoGerador_NaoADoCalculo()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, Jun2026, "Lei n.º 14/23");
        serie.Introduce(17m, Jul2026, null, "Lei n.º 20/26");

        var facto = new DateOnly(2026, 2, 10);

        Assert.Equal(14m, serie.InForceOn(facto)!.Percentage);
    }

    [Fact]
    public void VersaoFechada_ContinuaAVigorarParaAsDatasQueCobriu()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, Jun2026, "Lei n.º 14/23");

        // O fim é inclusivo, e fechar não apaga o passado.
        Assert.NotNull(serie.InForceOn(Jun2026));
    }

    [Fact]
    public void SemInstrumentoLegal_ARecusaEImediata()
    {
        var serie = Normal();

        Assert.Throws<ArgumentException>(() =>
            serie.Introduce(14m, Jan2026, null, "   "));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void TaxaForaDeZeroACem_ERecusada(decimal percentagem)
    {
        var serie = Normal();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            serie.Introduce(percentagem, Jan2026, null, "Lei n.º 14/23"));
    }

    [Fact]
    public void VigenciaQueTerminaAntesDeComecar_ERecusada()
    {
        var serie = Normal();

        Assert.Throws<ArgumentException>(() =>
            serie.Introduce(14m, Jul2026, Jan2026, "Lei n.º 14/23"));
    }

    [Theory]
    [InlineData("ISE")]
    [InlineData("NS")]
    [InlineData("ise")]
    public void CodigoDeIsencao_ExigeCodigoDeIsencao(string codigo)
    {
        var serie = TaxRateSchedule.Open(TaxKind.ValueAdded, codigo, "Isento");

        Assert.True(serie.RequiresExemptionCode);
    }

    /// <summary>
    /// Uma isenção com taxa é contradição: ou o código isenta, ou há imposto a
    /// liquidar.
    /// </summary>
    [Fact]
    public void IsencaoComTaxaDiferenteDeZero_ERecusada()
    {
        var serie = TaxRateSchedule.Open(TaxKind.ValueAdded, TaxCodes.Exempt, "Isento");

        Assert.Throws<ArgumentException>(() =>
            serie.Introduce(14m, Jan2026, null, "Lei n.º 14/23"));
    }

    [Fact]
    public void IsencaoComTaxaZero_EAceite()
    {
        var serie = TaxRateSchedule.Open(TaxKind.ValueAdded, TaxCodes.Exempt, "Isento");
        serie.Introduce(0m, Jan2026, null, "Lei n.º 14/23");

        Assert.Equal(0m, serie.InForceOn(Jan2026)!.Percentage);
    }

    [Fact]
    public void CodigoNormal_NaoExigeCodigoDeIsencao()
    {
        Assert.False(Normal().RequiresExemptionCode);
    }

    [Fact]
    public void CodigoEGuardadoEmMaiusculas()
    {
        var serie = TaxRateSchedule.Open(TaxKind.ValueAdded, " nor ", "IVA");

        Assert.Equal("NOR", serie.Code);
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var serie = Normal();
        serie.Introduce(14m, Jan2026, Jun2026, "Lei n.º 14/23");

        // Zero, e é o correcto: o incremento é do SaveChangesAsync do
        // DbContext. Se algum dia isto falhar, alguém repôs `Version++` no
        // domínio e o contador passa a subir duas vezes por escrita.
        Assert.Equal(0, serie.Version);
    }

    [Theory]
    [InlineData("NOR")]
    [InlineData("RED")]
    [InlineData("INT")]
    [InlineData("ISE")]
    [InlineData("OUT")]
    [InlineData("NS")]
    [InlineData("NA")]
    [InlineData("14")]
    [InlineData("14.5")]
    public void CodigosQueOSaftAceita_AbremSerie(string codigo)
    {
        var serie = TaxRateSchedule.Open(TaxKind.ValueAdded, codigo, "IVA");

        Assert.Equal(codigo, serie.Code);
    }

    [Theory]
    [InlineData("NORMAL")]
    [InlineData("IVA14")]
    [InlineData("TAXA-NORMAL")]
    public void CodigoDeIvaQueOSaftNaoAceita_ERecusadoAoAbrir(string codigo)
    {
        // Falha aqui e não no dia da entrega à AGT — a mesma lição do ADR-058
        // sobre o comprimento do NIF. Quem escreve o código está a olhar para
        // ele; meses depois, ninguém sabe o que quis dizer.
        var erro = Assert.Throws<ArgumentException>(
            () => TaxRateSchedule.Open(TaxKind.ValueAdded, codigo, "IVA"));

        Assert.Contains(codigo, erro.Message);
    }

    [Fact]
    public void OInssNaoESujeitoARegraDoSaft()
    {
        // `INSS` não passa no padrão do SAF-T, e é suposto não passar: não é
        // código de imposto da tabela, e as séries de INSS não entram na
        // exportação. Aplicar-lhes a regra impediria `payroll` de existir.
        var serie = TaxRateSchedule.Open(
            TaxKind.EmployeeSocialSecurity, TaxCodes.SocialSecurity, "INSS do trabalhador");

        Assert.Equal("INSS", serie.Code);
    }

    [Fact]
    public void CorrigirOCodigo_MudaOCodigo()
    {
        // A saída do beco: séries abertas antes da regra bloqueavam a
        // exportação para sempre, porque nada se elimina (BR-14).
        var serie = Normal();

        serie.CorrectCode("red");

        Assert.Equal("RED", serie.Code);
    }

    [Fact]
    public void CorrigirParaUmCodigoInvalido_ERecusado()
    {
        var serie = Normal();

        Assert.Throws<ArgumentException>(() => serie.CorrectCode("NORMAL"));

        // E não deixou a série a meio: o código anterior continua lá.
        Assert.Equal("NOR", serie.Code);
    }

    [Fact]
    public void CorrigirOCodigo_NaoTocaNasVersoes()
    {
        // As versões são o histórico de taxas, e a correcção é da etiqueta e
        // não do que vigorou. Documentos já emitidos guardam a taxa que
        // vigorava — mesma razão de `Customer.CorrectTaxId`.
        var serie = Normal();
        serie.Introduce(14m, Jan2026, null, "Lei n.º 14/23");

        serie.CorrectCode("RED");

        var versao = Assert.Single(serie.Versions);

        Assert.Equal(14m, versao.Percentage);
        Assert.Equal(Jan2026, versao.EffectiveFrom);
    }
}
