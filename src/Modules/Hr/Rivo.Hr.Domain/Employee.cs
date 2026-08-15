namespace Rivo.Hr.Domain;

/// <summary>
/// Colaborador — a pessoa na relação de trabalho.
///
/// <para>
/// <strong>Distinto de Utilizador</strong>, que pertence a `identity`
/// (ADR-004). A ligação é opcional nos dois sentidos: um colaborador pode não
/// ter login (trabalhador sem acesso ao sistema), e um utilizador pode não ser
/// colaborador (conta de sistema).
/// </para>
///
/// <para>
/// É a entidade com maior fan-out de todo o Rivo. Por isso o acesso externo
/// passa exclusivamente pelo contrato <c>EmployeeReference</c> (ADR-010) — o
/// modelo interno pode evoluir sem obrigar a mexer noutros módulos.
/// </para>
/// </summary>
public sealed class Employee
{
    private Employee() => FullName = string.Empty;

    private Employee(Guid id, string fullName, Guid? departmentId, Guid? userId, DateTimeOffset hiredOn)
    {
        Id = id;
        FullName = fullName;
        DepartmentId = departmentId;
        UserId = userId;
        HiredOn = hiredOn;
        Status = EmployeeStatus.Active;
    }

    public Guid Id { get; private set; }

    public string FullName { get; private set; }

    public Guid? DepartmentId { get; private set; }

    /// <summary>Conta em `identity`, se existir. Guardado como identificador, sem chave estrangeira entre schemas de módulos distintos além da PK (ADR-010).</summary>
    public Guid? UserId { get; private set; }

    public DateTimeOffset HiredOn { get; private set; }

    public EmployeeStatus Status { get; private set; }

    public static Employee Hire(string fullName, Guid? departmentId, Guid? userId, DateTimeOffset hiredOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new Employee(Guid.CreateVersion7(), fullName.Trim(), departmentId, userId, hiredOn);
    }

    public void MoveToDepartment(Guid? departmentId) => DepartmentId = departmentId;

    public void LinkToUser(Guid? userId) => UserId = userId;

    /// <summary>
    /// Desactiva o colaborador. Não elimina: o registo é referenciado por
    /// atribuições de cargo históricas e pela trilha de auditoria, que têm de
    /// continuar legíveis (BR-14).
    /// </summary>
    public void Deactivate() => Status = EmployeeStatus.Inactive;

    public void Reactivate() => Status = EmployeeStatus.Active;
}

public enum EmployeeStatus
{
    Active,
    Inactive,
}
