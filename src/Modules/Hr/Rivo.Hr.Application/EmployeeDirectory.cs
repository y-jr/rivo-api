using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application;

/// <summary>
/// Implementa o contrato publicado de `hr`.
///
/// É o único caminho por onde outros módulos lêem dados de colaborador — não
/// há acesso directo às tabelas (ADR-010).
/// </summary>
public sealed class EmployeeDirectory(IHrStore store, HireEmployee hire) : IEmployeeDirectory
{
    public async Task<EmployeeReference?> FindAsync(
        Guid employeeId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        return employee is null ? null : await ToReferenceAsync(employee, asOf, cancellationToken);
    }

    /// <summary>
    /// Primeiro consumidor: o Portal do Colaborador, para resolver "o
    /// próprio" (ADR-042). Mesma leitura de <see cref="FindAsync"/>, só
    /// entrando pela conta em vez do colaborador.
    /// </summary>
    public async Task<EmployeeReference?> FindByUserIdAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeByUserIdAsync(userId, cancellationToken);

        return employee is null ? null : await ToReferenceAsync(employee, asOf, cancellationToken);
    }

    private async Task<EmployeeReference> ToReferenceAsync(
        Employee employee,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var assignments = await store.ListAssignmentsForEmployeeAsync(employee.Id, cancellationToken);
        var position = await ResolvePositionAsync(assignments, asOf, cancellationToken);

        return Map(employee, position);
    }

    public async Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(
        Guid positionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var position = await store.FindPositionAsync(positionId, cancellationToken);

        if (position is null)
        {
            return [];
        }

        var assignments = await store.ListAssignmentsForPositionAsync(positionId, cancellationToken);

        // Só atribuições efectivas à data: uma pendente não confere o cargo,
        // e é isso que impede que uma submissão por aprovar já dê autoridade.
        var holders = assignments
            .Where(assignment => assignment.IsEffectiveAt(asOf))
            .Select(assignment => assignment.EmployeeId)
            .Distinct()
            .ToList();

        var references = new List<EmployeeReference>(holders.Count);

        foreach (var employeeId in holders)
        {
            var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

            if (employee is not null)
            {
                references.Add(Map(employee, ToReference(position)));
            }
        }

        return references;
    }

    public async Task<EmployeeHireResult> HireAsync(
        string fullName,
        string? departmentName,
        DateTimeOffset hiredOn,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return EmployeeHireResult.Rejected("Um colaborador precisa de nome.");
        }

        Guid? departmentId = null;

        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            var departamentos = await store.ListDepartmentsAsync(cancellationToken);
            var departamento = departamentos.FirstOrDefault(
                d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase));

            if (departamento is null)
            {
                return EmployeeHireResult.DepartmentNotFound(departmentName);
            }

            departmentId = departamento.Id;
        }

        var result = await hire.ExecuteAsync(
            fullName, departmentId, userId: null, hiredOn,
            new AuditContext(actorId, null, null), cancellationToken);

        return result.Outcome switch
        {
            HireEmployeeOutcome.Hired => EmployeeHireResult.Success(result.EmployeeId!.Value),
            _ => EmployeeHireResult.Rejected(result.Error ?? "Não foi possível contratar o colaborador."),
        };
    }

    private async Task<PositionReference?> ResolvePositionAsync(
        IReadOnlyList<PositionAssignment> assignments,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var current = assignments.FirstOrDefault(assignment => assignment.IsEffectiveAt(asOf));

        if (current is null)
        {
            return null;
        }

        var position = await store.FindPositionAsync(current.PositionId, cancellationToken);

        return position is null ? null : ToReference(position);
    }

    private static PositionReference ToReference(Position position) =>
        new(position.Id, position.Name, position.GrantsApprovalAuthority);

    private static EmployeeReference Map(Employee employee, PositionReference? position) =>
        new(
            employee.Id,
            employee.FullName,
            employee.Status is Domain.EmployeeStatus.Active
                ? Contracts.EmployeeStatus.Active
                : Contracts.EmployeeStatus.Inactive,
            employee.DepartmentId,
            position,
            employee.UserId);
}
