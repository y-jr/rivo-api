using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

public class BenefitTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static Benefit Health() =>
        Benefit.Create("Seguro de saúde", "Saude", 35_000m, "AOA", "Cobertura familiar");

    [Fact]
    public void Create_NormalisesKindAndCurrency()
    {
        var benefit = Health();

        Assert.Equal("saude", benefit.Kind);
        Assert.Equal("AOA", benefit.Currency);
        Assert.True(benefit.IsActive);
    }

    /// <summary>
    /// Zero é válido: nem todo o benefício tem contrapartida monetária — dias
    /// de férias extra, por exemplo.
    /// </summary>
    [Fact]
    public void Create_WithZeroValue_IsAllowed()
    {
        var benefit = Benefit.Create("Dia de aniversário", "tempo", 0m, "AOA");

        Assert.Equal(0m, benefit.MonthlyValue);
    }

    [Fact]
    public void Create_WithNegativeValue_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Benefit.Create("Inválido", "saude", -1m, "AOA"));
    }

    [Fact]
    public void Enrol_LinksEmployeeToBenefit()
    {
        var enrolment = BenefitEnrolment.Enrol(Employee, Health(), Start);

        Assert.Equal(BenefitEnrolmentStatus.Active, enrolment.Status);
        Assert.True(enrolment.IsActiveOn(Start));
    }

    /// <summary>
    /// Descontinuar um benefício impede adesões novas — sem cancelar as que já
    /// existem, que é o que separa "deixámos de oferecer" de "retirámos a quem
    /// já tinha".
    /// </summary>
    [Fact]
    public void Enrol_InDeactivatedBenefit_IsRejected()
    {
        var benefit = Health();
        var existing = BenefitEnrolment.Enrol(Employee, benefit, Start);

        benefit.Deactivate();

        Assert.Throws<InvalidOperationException>(() =>
            BenefitEnrolment.Enrol(Guid.CreateVersion7(), benefit, Start));

        // A adesão anterior sobrevive.
        Assert.True(existing.IsActiveOn(Start));
    }

    [Fact]
    public void Cancel_EndsTheEnrolment()
    {
        var enrolment = BenefitEnrolment.Enrol(Employee, Health(), Start);
        var day = new DateOnly(2026, 6, 30);

        enrolment.Cancel(day);

        Assert.Equal(BenefitEnrolmentStatus.Cancelled, enrolment.Status);
        Assert.False(enrolment.IsActiveOn(day));
        Assert.True(enrolment.IsActiveOn(day.AddDays(-1)));
    }

    [Fact]
    public void Cancel_BeforeItStarted_IsRejected()
    {
        var enrolment = BenefitEnrolment.Enrol(Employee, Health(), Start);

        Assert.Throws<ArgumentException>(() => enrolment.Cancel(Start.AddDays(-1)));
    }

    [Fact]
    public void Cancel_Twice_IsRejected()
    {
        var enrolment = BenefitEnrolment.Enrol(Employee, Health(), Start);
        enrolment.Cancel(new DateOnly(2026, 6, 30));

        Assert.Throws<InvalidOperationException>(() => enrolment.Cancel(new DateOnly(2026, 7, 31)));
    }
}
