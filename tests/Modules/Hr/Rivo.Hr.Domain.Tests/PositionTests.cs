using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Cargo — posição organizacional, distinta de Perfil de Acesso (ADR-005).
/// </summary>
public class PositionTests
{
    /// <summary>
    /// A marca mais sensível do módulo: `approval` resolve aprovadores por
    /// Cargo, logo quem controla esta marca controla quem pode aprovar
    /// pagamentos (BR-21). Tem de sobreviver intacta à criação.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_PreservesApprovalAuthority(bool grants)
    {
        var position = Position.Create("Director Financeiro", hierarchyLevel: 1, grantsApprovalAuthority: grants);

        Assert.Equal(grants, position.GrantsApprovalAuthority);
    }

    [Fact]
    public void Create_TrimsName()
    {
        var position = Position.Create("  Director Financeiro  ", 1, false);

        Assert.Equal("Director Financeiro", position.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Position.Create(name, 1, false));
    }

    [Fact]
    public void Create_WithNullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Position.Create(null!, 1, false));
    }

    [Fact]
    public void Create_WithNegativeHierarchyLevel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Position.Create("CEO", -1, true));
    }

    /// <summary>
    /// Menor é mais alto na hierarquia, logo zero é o topo e tem de ser
    /// aceite. A fronteira fica fixada para ninguém a "corrigir" para
    /// exigir positivo.
    /// </summary>
    [Fact]
    public void Create_WithHierarchyLevelZero_IsAccepted()
    {
        var position = Position.Create("CEO", 0, true);

        Assert.Equal(0, position.HierarchyLevel);
    }

    [Fact]
    public void Correct_ChangesNameAndLevel()
    {
        var position = Position.Create("Analsta", 5, grantsApprovalAuthority: false);

        position.Correct("  Analista  ", 4);

        Assert.Equal("Analista", position.Name);
        Assert.Equal(4, position.HierarchyLevel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Correct_NeverTouchesApprovalAuthority(bool confere)
    {
        var position = Position.Create("Director", 1, confere);

        position.Correct("Director-Geral", 1);

        // A marca de autoridade e o que o BR-20 vigia: se fosse editavel,
        // liga-la aqui daria autoridade a quem ja tem o cargo, sem que nenhuma
        // dessas atribuicoes passasse por aprovacao.
        Assert.Equal(confere, position.GrantsApprovalAuthority);
    }

    [Fact]
    public void Correct_RejectsNegativeLevel()
    {
        var position = Position.Create("Analista", 5, grantsApprovalAuthority: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => position.Correct("Analista", -1));
        Assert.Equal(5, position.HierarchyLevel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Correct_RejectsEmptyName(string nome)
    {
        var position = Position.Create("Analista", 5, grantsApprovalAuthority: false);

        Assert.Throws<ArgumentException>(() => position.Correct(nome, 5));
        Assert.Equal("Analista", position.Name);
    }
}
