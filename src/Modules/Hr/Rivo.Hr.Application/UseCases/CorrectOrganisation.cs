using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// As correcções de `hr` — o que se altera depois de criado (ADR-063).
///
/// <para>
/// <strong>Corrigir não é o mesmo que mudar de estado.</strong> Estes casos de
/// uso servem o engano de digitação e o dado que muda na vida real (um nome de
/// casada, um departamento reorganizado). Não servem para desligar alguém, que
/// é a cessação do contrato e tem o seu próprio caminho, nem para dar autoridade
/// de aprovação, que passa por governança (BR-20).
/// </para>
///
/// <para>
/// <strong>Todas registam o valor anterior na trilha.</strong> Uma correcção sem
/// o valor antigo não é auditável: fica a saber-se que algo mudou e não o quê —
/// e é precisamente numa correcção que a pergunta «o que dizia antes?» se faz.
/// </para>
/// </summary>
public sealed class CorrectEmployee(IHrStore store, IAuditTrail audit)
{
    public async Task<CorrectionResult> ExecuteAsync(
        Guid employeeId,
        string fullName,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return CorrectionResult.NotFound();
        }

        var anterior = employee.FullName;

        try
        {
            employee.CorrectName(fullName);
        }
        catch (ArgumentException error)
        {
            return CorrectionResult.Rejected(error.Message);
        }

        // Nada mudou: não se grava nem se audita uma não-alteração, que só
        // encheria a trilha de ruído onde alguém vai procurar uma mudança real.
        if (string.Equals(anterior, employee.FullName, StringComparison.Ordinal))
        {
            return CorrectionResult.Success();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeCorrected,
                HrAuditEntityTypes.Employee,
                employee.Id.ToString(),
                context,
                PreviousValue: $$"""{"fullName":{{Json(anterior)}}}""",
                NewValue: $$"""{"fullName":{{Json(employee.FullName)}}}"""),
            cancellationToken);

        return CorrectionResult.Success();
    }

    internal static string Json(string? valor) =>
        valor is null ? "null" : System.Text.Json.JsonSerializer.Serialize(valor);
}

/// <summary>
/// Transfere um colaborador de departamento.
///
/// <para>
/// <strong>Acto próprio, e não um campo de um formulário de correcção.</strong>
/// Mudar de departamento é uma decisão de organização com data — não é o mesmo
/// que corrigir um nome mal escrito, e a trilha tem de as poder distinguir.
/// </para>
/// </summary>
public sealed class TransferEmployee(IHrStore store, IAuditTrail audit)
{
    public async Task<CorrectionResult> ExecuteAsync(
        Guid employeeId,
        Guid? departmentId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return CorrectionResult.NotFound();
        }

        // Um departamento que não existe é engano de quem chama, e não um
        // colaborador sem departamento: distinguem-se, porque `null` é uma
        // escolha válida — há colaboradores sem departamento atribuído.
        if (departmentId is { } destino && !await store.DepartmentExistsAsync(destino, cancellationToken))
        {
            return CorrectionResult.Rejected("O departamento de destino não existe.");
        }

        var anterior = employee.DepartmentId;

        if (anterior == departmentId)
        {
            return CorrectionResult.Success();
        }

        employee.MoveToDepartment(departmentId);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeTransferred,
                HrAuditEntityTypes.Employee,
                employee.Id.ToString(),
                context,
                PreviousValue: $$"""{"departmentId":{{Quote(anterior)}}}""",
                NewValue: $$"""{"departmentId":{{Quote(departmentId)}}}"""),
            cancellationToken);

        return CorrectionResult.Success();
    }

    private static string Quote(Guid? id) => id is null ? "null" : $"\"{id}\"";
}

public sealed class CorrectDepartment(IHrStore store, IAuditTrail audit)
{
    public async Task<CorrectionResult> ExecuteAsync(
        Guid departmentId,
        string name,
        Guid? managerId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var department = await store.FindDepartmentAsync(departmentId, cancellationToken);

        if (department is null)
        {
            return CorrectionResult.NotFound();
        }

        // O responsável tem de ser alguém que existe. `null` continua a ser
        // válido: um departamento pode não ter responsável designado.
        if (managerId is { } gestor && await store.FindEmployeeAsync(gestor, cancellationToken) is null)
        {
            return CorrectionResult.Rejected("O responsável indicado não é um colaborador conhecido.");
        }

        var nomeAnterior = department.Name;
        var gestorAnterior = department.ManagerId;

        try
        {
            department.Rename(name);
        }
        catch (ArgumentException error)
        {
            return CorrectionResult.Rejected(error.Message);
        }

        department.AssignManager(managerId);

        if (string.Equals(nomeAnterior, department.Name, StringComparison.Ordinal)
            && gestorAnterior == managerId)
        {
            return CorrectionResult.Success();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.DepartmentCorrected,
                HrAuditEntityTypes.Department,
                department.Id.ToString(),
                context,
                PreviousValue: $$"""{"name":{{CorrectEmployee.Json(nomeAnterior)}},"managerId":{{Quote(gestorAnterior)}}}""",
                NewValue: $$"""{"name":{{CorrectEmployee.Json(department.Name)}},"managerId":{{Quote(managerId)}}}"""),
            cancellationToken);

        return CorrectionResult.Success();
    }

    private static string Quote(Guid? id) => id is null ? "null" : $"\"{id}\"";
}

/// <summary>
/// Corrige o nome e o nível de um cargo.
///
/// <para>
/// Protegido por <c>hr.positions.write</c>, que só o Admin tem (ADR-015) — a
/// mesma permissão que cria cargos, e pela mesma razão: quem mexe no catálogo
/// mexe, indirectamente, na cadeia de autoridade. <strong>A marca de autoridade
/// não se altera aqui</strong>, e o porquê está em <c>Position.Correct</c>.
/// </para>
/// </summary>
public sealed class CorrectPosition(IHrStore store, IAuditTrail audit)
{
    public async Task<CorrectionResult> ExecuteAsync(
        Guid positionId,
        string name,
        int hierarchyLevel,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var position = await store.FindPositionAsync(positionId, cancellationToken);

        if (position is null)
        {
            return CorrectionResult.NotFound();
        }

        var nomeAnterior = position.Name;
        var nivelAnterior = position.HierarchyLevel;

        try
        {
            position.Correct(name, hierarchyLevel);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return CorrectionResult.Rejected(error.Message);
        }

        if (string.Equals(nomeAnterior, position.Name, StringComparison.Ordinal)
            && nivelAnterior == position.HierarchyLevel)
        {
            return CorrectionResult.Success();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionCorrected,
                HrAuditEntityTypes.Position,
                position.Id.ToString(),
                context,
                PreviousValue: $$"""{"name":{{CorrectEmployee.Json(nomeAnterior)}},"hierarchyLevel":{{nivelAnterior}}}""",
                NewValue: $$"""{"name":{{CorrectEmployee.Json(position.Name)}},"hierarchyLevel":{{position.HierarchyLevel}}}"""),
            cancellationToken);

        return CorrectionResult.Success();
    }
}

public enum CorrectionOutcome
{
    Corrected,

    /// <summary>O registo não existe — 404, e não 400.</summary>
    NotFound,

    /// <summary>O pedido está mal formado ou refere algo que não existe — 400.</summary>
    Rejected,
}

public sealed record CorrectionResult(CorrectionOutcome Outcome, string? Error = null)
{
    public static CorrectionResult Success() => new(CorrectionOutcome.Corrected);

    public static CorrectionResult NotFound() => new(CorrectionOutcome.NotFound);

    public static CorrectionResult Rejected(string error) => new(CorrectionOutcome.Rejected, error);
}
