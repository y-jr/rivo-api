using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Contracts;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

public sealed class ListEmployees(IHrStore store)
{
    public async Task<IReadOnlyList<EmployeeView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var employees = await store.ListEmployeesAsync(cancellationToken);

        return [.. employees.Select(e => new EmployeeView(
            e.Id, e.FullName, e.Status.ToString(), e.DepartmentId, e.UserId, e.HiredOn))];
    }
}

public sealed record EmployeeView(
    Guid EmployeeId,
    string FullName,
    string Status,
    Guid? DepartmentId,
    Guid? UserId,
    DateTimeOffset HiredOn);

public sealed class HireEmployee(IHrStore store, IAuditTrail audit)
{
    public async Task<HireEmployeeResult> ExecuteAsync(
        string fullName,
        Guid? departmentId,
        Guid? userId,
        DateTimeOffset hiredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Departamento desconhecido é recusado em vez de aceite como nulo:
        // aceitar em silêncio deixaria o colaborador fora do organograma sem
        // ninguém reparar.
        if (departmentId is not null && !await store.DepartmentExistsAsync(departmentId.Value, cancellationToken))
        {
            return HireEmployeeResult.DepartmentNotFound();
        }

        var employee = Employee.Hire(fullName, departmentId, userId, hiredOn);

        await store.AddEmployeeAsync(employee, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeHired,
                HrAuditEntityTypes.Employee,
                employee.Id.ToString(),
                context),
            cancellationToken);

        return HireEmployeeResult.Success(employee.Id);
    }
}

public sealed record HireEmployeeResult(bool Succeeded, Guid? EmployeeId, string? Error)
{
    public static HireEmployeeResult Success(Guid id) => new(true, id, null);

    public static HireEmployeeResult DepartmentNotFound() =>
        new(false, null, "Departamento não encontrado.");
}

/// <summary>Acções de `hr` registadas na trilha de auditoria.</summary>
public static class HrAuditActions
{
    public const string EmployeeHired = "hr.employee.hired";
    public const string DepartmentCreated = "hr.department.created";
    public const string PositionCreated = "hr.position.created";
    public const string PositionAssigned = "hr.position.assigned";
    public const string DocumentAttached = "hr.employee.document_attached";
}

public static class HrAuditEntityTypes
{
    public const string Employee = "hr.employee";
    public const string Department = "hr.department";
    public const string Position = "hr.position";
}

