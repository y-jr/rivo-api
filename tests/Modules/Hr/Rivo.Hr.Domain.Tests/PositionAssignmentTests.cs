using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Atribuição de Cargo — o agregado mais sensível de `hr`.
///
/// <para>
/// Duas invariantes justificam sozinhas a existência deste ficheiro:
/// uma atribuição pendente <strong>não confere o Cargo</strong> (ADR-015,
/// BR-20), e a vigência é histórica para que `approval` possa perguntar quem
/// ocupava o Cargo <em>à data da submissão</em> e não hoje (BR-6).
/// </para>
/// </summary>
public class PositionAssignmentTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid Position = Guid.CreateVersion7();
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // --- Estado inicial ---------------------------------------------------

    [Fact]
    public void CreateEffective_StartsEffective()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);

        Assert.Equal(PositionAssignmentStatus.Effective, assignment.Status);
    }

    [Fact]
    public void CreatePending_StartsPending()
    {
        var assignment = PositionAssignment.CreatePending(Employee, Position, From, null);

        Assert.Equal(PositionAssignmentStatus.Pending, assignment.Status);
    }

    /// <summary>
    /// A invariante que fecha a escalada de privilégios do ADR-015.
    ///
    /// Se este teste passar com a verificação de estado apagada de
    /// <c>IsEffectiveAt</c>, a atribuição pendente passaria a conferir o
    /// Cargo — e submeter o pedido bastaria para ganhar autoridade de
    /// aprovação, sem ninguém decidir nada.
    /// </summary>
    [Fact]
    public void IsEffectiveAt_PendingAssignmentInsideItsPeriod_GrantsNothing()
    {
        var assignment = PositionAssignment.CreatePending(Employee, Position, From, null);

        Assert.False(assignment.IsEffectiveAt(From.AddDays(30)));
    }

    // --- Vigência ---------------------------------------------------------

    [Fact]
    public void IsEffectiveAt_InsideOpenEndedPeriod_IsTrue()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);

        Assert.True(assignment.IsEffectiveAt(From.AddYears(5)));
    }

    [Fact]
    public void IsEffectiveAt_BeforeItStarts_IsFalse()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);

        Assert.False(assignment.IsEffectiveAt(From.AddTicks(-1)));
    }

    /// <summary>O início é inclusivo: quem toma posse a 1 de Janeiro ocupa o Cargo nesse dia.</summary>
    [Fact]
    public void IsEffectiveAt_ExactlyAtStart_IsTrue()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);

        Assert.True(assignment.IsEffectiveAt(From));
    }

    /// <summary>
    /// O fim é exclusivo. Fixado em teste de propósito: se as fronteiras
    /// fossem ambas inclusivas, duas atribuições consecutivas do mesmo Cargo
    /// sobrepor-se-iam num instante, e `approval` encontraria dois ocupantes.
    /// </summary>
    [Fact]
    public void IsEffectiveAt_ExactlyAtEnd_IsFalse()
    {
        var to = From.AddYears(1);
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, to);

        Assert.False(assignment.IsEffectiveAt(to));
        Assert.True(assignment.IsEffectiveAt(to.AddTicks(-1)));
    }

    // --- Validação --------------------------------------------------------

    [Fact]
    public void Create_WithoutEmployee_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PositionAssignment.CreateEffective(Guid.Empty, Position, From, null));
    }

    [Fact]
    public void Create_WithoutPosition_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PositionAssignment.CreateEffective(Employee, Guid.Empty, From, null));
    }

    [Fact]
    public void Create_EndingBeforeItStarts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PositionAssignment.CreateEffective(Employee, Position, From, From.AddDays(-1)));
    }

    /// <summary>Um período de duração nula não é ocupação nenhuma.</summary>
    [Fact]
    public void Create_EndingExactlyWhenItStarts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PositionAssignment.CreateEffective(Employee, Position, From, From));
    }

    /// <summary>A validação aplica-se aos dois caminhos de criação, não só ao efectivo.</summary>
    [Fact]
    public void CreatePending_WithoutEmployee_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PositionAssignment.CreatePending(Guid.Empty, Position, From, null));
    }

    // --- Fim da ocupação --------------------------------------------------

    [Fact]
    public void End_SetsTheEndOfThePeriod()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);
        var endsAt = From.AddMonths(6);

        assignment.End(endsAt);

        Assert.Equal(endsAt, assignment.EffectiveTo);
        Assert.False(assignment.IsEffectiveAt(endsAt));
    }

    [Fact]
    public void End_BeforeItStarted_Throws()
    {
        var assignment = PositionAssignment.CreateEffective(Employee, Position, From, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => assignment.End(From.AddTicks(-1)));
    }
}
