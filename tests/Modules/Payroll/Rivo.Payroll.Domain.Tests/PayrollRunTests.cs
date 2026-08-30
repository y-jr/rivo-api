using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Domain.Tests;

/// <summary>
/// A folha em si — abertura, itens e o ciclo Draft → PendingApproval →
/// Approved/Refused. O cálculo fiscal (INSS/IRT) não vive aqui: é `fiscal`
/// que o determina, `AddPayrollItem` (Application) que pergunta, e este
/// agregado só aplica o resultado via <see cref="PayrollItem.ApplyCalculation"/>
/// — ver <see cref="PayrollItemCalculationTests"/>.
/// </summary>
public class PayrollRunTests
{
    private static PayrollRun Aberta(int year = 2026, int month = 8) =>
        PayrollRun.Open(year, month, Guid.CreateVersion7());

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void MesForaDeUmADoze_ERecusado(int mes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PayrollRun.Open(2026, mes, Guid.CreateVersion7()));
    }

    [Theory]
    [InlineData(2026, 2, 28)] // 2026 não é bissexto
    [InlineData(2026, 8, 31)]
    [InlineData(2026, 4, 30)]
    public void PeriodEndDate_EOUltimoDiaDoMes(int ano, int mes, int diaEsperado)
    {
        var folha = PayrollRun.Open(ano, mes, Guid.CreateVersion7());

        Assert.Equal(new DateOnly(ano, mes, diaEsperado), folha.PeriodEndDate);
    }

    [Fact]
    public void ItemAcrescentado_ContaParaOTotalBruto()
    {
        var folha = Aberta();
        folha.AddItem(Guid.CreateVersion7(), 250_000m);
        folha.AddItem(Guid.CreateVersion7(), 100_000m);

        Assert.Equal(350_000m, folha.TotalGross);
    }

    [Fact]
    public void SalarioBrutoNaoPositivo_ERecusado()
    {
        var folha = Aberta();

        Assert.Throws<ArgumentOutOfRangeException>(() => folha.AddItem(Guid.CreateVersion7(), 0m));
    }

    [Fact]
    public void SoSeAcrescentaItemAUmRascunho()
    {
        var folha = Aberta();
        folha.AddItem(Guid.CreateVersion7(), 250_000m);
        folha.MarkSubmitted(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => folha.AddItem(Guid.CreateVersion7(), 100_000m));
    }

    [Fact]
    public void SoUmRascunhoSeSubmete()
    {
        var folha = Aberta();
        folha.AddItem(Guid.CreateVersion7(), 250_000m);
        folha.MarkSubmitted(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            folha.MarkSubmitted(Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FolhaSemItens_NaoSeSubmete()
    {
        var folha = Aberta();

        Assert.Throws<InvalidOperationException>(() =>
            folha.MarkSubmitted(Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var folha = Aberta();
        folha.AddItem(Guid.CreateVersion7(), 250_000m);

        Assert.Equal(0, folha.Version);
    }
}

/// <summary>
/// <see cref="PayrollItem.ApplyCalculation"/> — o líquido calcula-se aqui,
/// nunca se recebe como parâmetro. Ver o comentário na própria classe.
/// </summary>
public class PayrollItemCalculationTests
{
    private static PayrollItem NovoItem(decimal grossSalary = 250_000m)
    {
        var folha = PayrollRun.Open(2026, 8, Guid.CreateVersion7());
        return folha.AddItem(Guid.CreateVersion7(), grossSalary);
    }

    /// <summary>O exemplo de `docs/rivo-fiscal-regras-angola-v1.md` §1.6, ponta a ponta.</summary>
    [Fact]
    public void ExemploDocumentado_Bruto250000_Liquido203600()
    {
        var item = NovoItem(250_000m);

        item.ApplyCalculation(withholdingTax: 38_900m, socialSecurityContribution: 7_500m);

        Assert.Equal(38_900m, item.WithholdingTax);
        Assert.Equal(7_500m, item.SocialSecurityContribution);
        Assert.Equal(203_600m, item.NetSalary);
    }

    /// <summary>
    /// A invariante fica verdadeira por construção: não há como o líquido
    /// discordar do bruto menos os descontos, porque não se recebe um
    /// terceiro número — calcula-se sempre a partir dos outros dois.
    /// </summary>
    [Fact]
    public void LiquidoENumeroCalculado_NuncaRecebido()
    {
        var item = NovoItem(100_000m);

        item.ApplyCalculation(withholdingTax: 10_000m, socialSecurityContribution: 3_000m);

        Assert.Equal(item.GrossSalary - item.WithholdingTax - item.SocialSecurityContribution, item.NetSalary);
    }

    [Fact]
    public void IrtNegativo_ERecusado()
    {
        var item = NovoItem();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            item.ApplyCalculation(withholdingTax: -1m, socialSecurityContribution: 7_500m));
    }

    [Fact]
    public void InssNegativo_ERecusado()
    {
        var item = NovoItem();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            item.ApplyCalculation(withholdingTax: 38_900m, socialSecurityContribution: -1m));
    }

    [Fact]
    public void AntesDoCalculo_OsTresCamposFicamNulos()
    {
        var item = NovoItem();

        Assert.Null(item.NetSalary);
        Assert.Null(item.WithholdingTax);
        Assert.Null(item.SocialSecurityContribution);
    }
}
