using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Colaborador — a entidade com maior fan-out do Rivo (ADR-010).
/// </summary>
public class EmployeeTests
{
    private static readonly DateTimeOffset HiredOn = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hire_StartsActive()
    {
        var employee = Employee.Hire("Ana Kiala", null, null, HiredOn);

        Assert.Equal(EmployeeStatus.Active, employee.Status);
    }

    [Fact]
    public void Hire_TrimsFullName()
    {
        var employee = Employee.Hire("  Ana Kiala  ", null, null, HiredOn);

        Assert.Equal("Ana Kiala", employee.FullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hire_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Employee.Hire(name, null, null, HiredOn));
    }

    /// <summary>
    /// A ligação a uma conta de `identity` é opcional nos dois sentidos
    /// (ADR-004): há colaboradores sem acesso ao sistema. Contratar sem
    /// utilizador tem de ser possível — o protótipo não distinguia os dois
    /// conceitos, e isso foi o anti-padrão A9.
    /// </summary>
    [Fact]
    public void Hire_WithoutUserAccount_IsAllowed()
    {
        var employee = Employee.Hire("Ana Kiala", departmentId: null, userId: null, HiredOn);

        Assert.Null(employee.UserId);
    }

    [Fact]
    public void Hire_WithoutDepartment_IsAllowed()
    {
        var employee = Employee.Hire("Ana Kiala", departmentId: null, userId: null, HiredOn);

        Assert.Null(employee.DepartmentId);
    }

    /// <summary>
    /// BR-14: desactivar não elimina. O registo continua referenciado por
    /// atribuições históricas e pela trilha de auditoria, que têm de
    /// permanecer legíveis — por isso a identidade e a data de admissão têm
    /// de sobreviver à desactivação.
    /// </summary>
    [Fact]
    public void Deactivate_PreservesIdentityAndHistory()
    {
        var employee = Employee.Hire("Ana Kiala", null, null, HiredOn);
        var id = employee.Id;

        employee.Deactivate();

        Assert.Equal(EmployeeStatus.Inactive, employee.Status);
        Assert.Equal(id, employee.Id);
        Assert.Equal("Ana Kiala", employee.FullName);
        Assert.Equal(HiredOn, employee.HiredOn);
    }

    [Fact]
    public void Reactivate_RestoresActiveStatus()
    {
        var employee = Employee.Hire("Ana Kiala", null, null, HiredOn);
        employee.Deactivate();

        employee.Reactivate();

        Assert.Equal(EmployeeStatus.Active, employee.Status);
    }

    [Fact]
    public void LinkToUser_AttachesAnAccountLater()
    {
        var employee = Employee.Hire("Ana Kiala", null, null, HiredOn);
        var userId = Guid.CreateVersion7();

        employee.LinkToUser(userId);

        Assert.Equal(userId, employee.UserId);
    }
}
