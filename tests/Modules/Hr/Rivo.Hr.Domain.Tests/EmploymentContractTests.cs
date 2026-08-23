using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Invariantes do Contrato de Trabalho.
///
/// <para>
/// O que aqui se testa é o que distingue um contrato de um formulário: um tipo
/// que manda na vigência, uma remuneração que não pode ser zero, e uma cessação
/// que não anda para trás no tempo.
/// </para>
/// </summary>
public class EmploymentContractTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static EmploymentContract Permanent() =>
        EmploymentContract.Draw(Employee, EmploymentContractType.Permanent, Start, null, 450_000m, "AOA");

    private static EmploymentContract FixedTerm() =>
        EmploymentContract.Draw(Employee, EmploymentContractType.FixedTerm, Start, End, 300_000m, "AOA");

    [Fact]
    public void Draw_WithValidTerms_StartsActive()
    {
        var contract = Permanent();

        Assert.Equal(EmploymentContractStatus.Active, contract.Status);
        Assert.Equal(Employee, contract.EmployeeId);
        Assert.Null(contract.EndsOn);
    }

    /// <summary>
    /// O tipo tem de mandar na vigência, senão é decoração. Um contrato sem
    /// termo com data de fim apareceria eternamente numa lista de contratos a
    /// expirar que nunca expiram.
    /// </summary>
    [Fact]
    public void Draw_PermanentWithEndDate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            EmploymentContract.Draw(
                Employee, EmploymentContractType.Permanent, Start, End, 450_000m, "AOA"));
    }

    [Theory]
    [InlineData(EmploymentContractType.FixedTerm)]
    [InlineData(EmploymentContractType.Freelance)]
    public void Draw_TermTypeWithoutEndDate_IsRejected(EmploymentContractType type)
    {
        Assert.Throws<ArgumentException>(() =>
            EmploymentContract.Draw(Employee, type, Start, null, 300_000m, "AOA"));
    }

    [Fact]
    public void Draw_WithEndBeforeStart_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            EmploymentContract.Draw(
                Employee, EmploymentContractType.FixedTerm, End, Start, 300_000m, "AOA"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Draw_WithoutPositiveSalary_IsRejected(int salary)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmploymentContract.Draw(
                Employee, EmploymentContractType.Permanent, Start, null, salary, "AOA"));
    }

    /// <summary>
    /// A moeda é registada por extenso porque `docs` §5 fixa capacidade
    /// multi-moeda. Um código que não seja ISO 4217 tornaria a conversão em
    /// `finance` impossível de resolver.
    /// </summary>
    [Theory]
    [InlineData("KZ")]
    [InlineData("KWANZA")]
    [InlineData("  ")]
    public void Draw_WithInvalidCurrency_IsRejected(string currency)
    {
        Assert.Throws<ArgumentException>(() =>
            EmploymentContract.Draw(
                Employee, EmploymentContractType.Permanent, Start, null, 450_000m, currency));
    }

    [Fact]
    public void Draw_NormalisesCurrencyToUpperCase()
    {
        var contract = EmploymentContract.Draw(
            Employee, EmploymentContractType.Permanent, Start, null, 450_000m, " aoa ");

        Assert.Equal("AOA", contract.Currency);
    }

    [Fact]
    public void IsInForceOn_RespectsTheAgreedWindow()
    {
        var contract = FixedTerm();

        Assert.False(contract.IsInForceOn(Start.AddDays(-1)));
        Assert.True(contract.IsInForceOn(Start));
        Assert.True(contract.IsInForceOn(End));
        Assert.False(contract.IsInForceOn(End.AddDays(1)));
    }

    [Fact]
    public void IsInForceOn_PermanentContract_HasNoUpperBound()
    {
        var contract = Permanent();

        Assert.True(contract.IsInForceOn(Start.AddYears(30)));
    }

    [Fact]
    public void Terminate_ClosesTheContractOnThatDay()
    {
        var contract = Permanent();
        var day = new DateOnly(2026, 6, 30);

        contract.Terminate(day);

        Assert.Equal(EmploymentContractStatus.Terminated, contract.Status);
        Assert.Equal(day, contract.EndsOn);

        // A data de cessação é o último dia, inclusive — a mesma semântica da
        // data de fim de um contrato a termo. Deixa de vigorar no dia seguinte.
        Assert.True(contract.IsInForceOn(day));
        Assert.False(contract.IsInForceOn(day.AddDays(1)));
    }

    [Fact]
    public void Terminate_BeforeItStarted_IsRejected()
    {
        var contract = Permanent();

        Assert.Throws<ArgumentException>(() => contract.Terminate(Start.AddDays(-1)));
    }

    [Fact]
    public void Terminate_Twice_IsRejected()
    {
        var contract = Permanent();
        contract.Terminate(new DateOnly(2026, 6, 30));

        Assert.Throws<InvalidOperationException>(() => contract.Terminate(new DateOnly(2026, 7, 31)));
    }

    /// <summary>
    /// É esta regra que impede duas relações laborais simultâneas com a mesma
    /// pessoa — a verificação corre no caso de uso, que é quem vê os outros
    /// contratos, mas o critério vive aqui.
    /// </summary>
    [Fact]
    public void OverlapsWith_DetectsSimultaneousContracts()
    {
        var contract = FixedTerm();

        Assert.True(contract.OverlapsWith(new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1)));
        Assert.True(contract.OverlapsWith(new DateOnly(2025, 1, 1), null));
        Assert.False(contract.OverlapsWith(new DateOnly(2027, 1, 1), null));
        Assert.False(contract.OverlapsWith(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
    }

    /// <summary>
    /// Cessar encurta a vigência — não apaga o período que correu.
    ///
    /// <para>
    /// É o que permite recontratar depois da cessação, sem deixar celebrar um
    /// contrato retroactivo por cima de meses que já tiveram outro. `payroll`
    /// encontraria dois contratos em vigor no mesmo mês.
    /// </para>
    /// </summary>
    [Fact]
    public void OverlapsWith_TerminatedContract_StillOccupiesThePeriodItRan()
    {
        var contract = FixedTerm();
        contract.Terminate(new DateOnly(2026, 3, 31));

        // Depois da cessação: livre — é a recontratação.
        Assert.False(contract.OverlapsWith(new DateOnly(2026, 4, 1), null));

        // Por cima dos meses que correram: continua a colidir.
        Assert.True(contract.OverlapsWith(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    /// <summary>
    /// Uma vigência passada continua a ser consultável, que é o que `payroll`
    /// precisa para processar um mês anterior à cessação.
    /// </summary>
    [Fact]
    public void IsInForceOn_TerminatedContract_AnswersForPastDates()
    {
        var contract = FixedTerm();
        contract.Terminate(new DateOnly(2026, 3, 31));

        Assert.True(contract.IsInForceOn(new DateOnly(2026, 2, 15)));
        Assert.False(contract.IsInForceOn(new DateOnly(2026, 4, 15)));
    }

    [Fact]
    public void ReviseSalary_ChangesTheAgreedAmount()
    {
        var contract = Permanent();

        contract.ReviseSalary(520_000m, "AOA");

        Assert.Equal(520_000m, contract.MonthlySalary);
    }

    [Fact]
    public void ReviseSalary_OnTerminatedContract_IsRejected()
    {
        var contract = Permanent();
        contract.Terminate(new DateOnly(2026, 6, 30));

        Assert.Throws<InvalidOperationException>(() => contract.ReviseSalary(520_000m, "AOA"));
    }
}
