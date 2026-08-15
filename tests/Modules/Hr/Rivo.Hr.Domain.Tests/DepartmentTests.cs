using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Departamento — unidade organizacional, distinta de Centro de Custo, que
/// pertence a `finance` (ADR-005).
/// </summary>
public class DepartmentTests
{
    [Fact]
    public void Create_TrimsName()
    {
        var department = Department.Create("  Financeiro  ", null);

        Assert.Equal("Financeiro", department.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Department.Create(name, null));
    }

    /// <summary>Um departamento pode existir antes de lhe ser atribuído um gestor.</summary>
    [Fact]
    public void Create_WithoutManager_IsAllowed()
    {
        var department = Department.Create("Financeiro", managerId: null);

        Assert.Null(department.ManagerId);
    }

    [Fact]
    public void Rename_TrimsAndReplacesTheName()
    {
        var department = Department.Create("Financeiro", null);

        department.Rename("  Financeiro e Administrativo  ");

        Assert.Equal("Financeiro e Administrativo", department.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_ToNothing_Throws(string name)
    {
        var department = Department.Create("Financeiro", null);

        Assert.Throws<ArgumentException>(() => department.Rename(name));
    }

    [Fact]
    public void AssignManager_SetsTheManager()
    {
        var department = Department.Create("Financeiro", null);
        var manager = Guid.CreateVersion7();

        department.AssignManager(manager);

        Assert.Equal(manager, department.ManagerId);
    }
}
