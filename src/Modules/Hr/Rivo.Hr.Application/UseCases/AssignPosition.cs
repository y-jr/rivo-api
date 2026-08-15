using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Atribui um Cargo a um colaborador.
///
/// <para>
/// Implementa BR-20: se o Cargo confere autoridade de aprovação, a atribuição
/// tem de passar por `approval` antes de produzir efeito. Sem esse controlo,
/// quem atribui Cargos decidiria quem aprova pagamentos sem tocar em perfis
/// nem permissões — escalada de privilégios invisível ao RBAC (ADR-015).
/// </para>
/// </summary>
public sealed class AssignPosition(IHrStore store, IAuditTrail audit)
{
    public async Task<AssignPositionResult> ExecuteAsync(
        Guid employeeId,
        Guid positionId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return AssignPositionResult.EmployeeNotFound();
        }

        var position = await store.FindPositionAsync(positionId, cancellationToken);

        if (position is null)
        {
            return AssignPositionResult.PositionNotFound();
        }

        if (position.GrantsApprovalAuthority)
        {
            // O módulo `approval` ainda não existe, logo não há como obter a
            // decisão que BR-20 exige.
            //
            // Recusa-se em vez de criar uma atribuição permanentemente
            // pendente ou — pior — de a tornar efectiva ignorando a regra.
            // Falhar aqui, ruidosamente, é o comportamento seguro: o caminho
            // de escalada continua fechado.
            return AssignPositionResult.RequiresApproval(position.Name);
        }

        var assignment = PositionAssignment.CreateEffective(
            employeeId, positionId, effectiveFrom, effectiveTo);

        await store.AddAssignmentAsync(assignment, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionAssigned,
                HrAuditEntityTypes.Employee,
                employeeId.ToString(),
                context,
                NewValue: $$"""{"positionId":"{{positionId}}","position":"{{position.Name}}","status":"Effective"}"""),
            cancellationToken);

        return AssignPositionResult.Assigned(assignment.Id);
    }
}

public sealed record AssignPositionResult(
    AssignPositionOutcome Outcome,
    Guid? AssignmentId,
    string? Message)
{
    public static AssignPositionResult Assigned(Guid id) =>
        new(AssignPositionOutcome.Assigned, id, null);

    public static AssignPositionResult EmployeeNotFound() =>
        new(AssignPositionOutcome.EmployeeNotFound, null, "Colaborador não encontrado.");

    public static AssignPositionResult PositionNotFound() =>
        new(AssignPositionOutcome.PositionNotFound, null, "Cargo não encontrado.");

    public static AssignPositionResult RequiresApproval(string positionName) =>
        new(
            AssignPositionOutcome.RequiresApproval,
            null,
            $"O cargo '{positionName}' confere autoridade de aprovação, pelo que a atribuição " +
            "tem de ser aprovada (BR-20). O módulo de aprovações ainda não está implementado.");
}

public enum AssignPositionOutcome
{
    Assigned,
    EmployeeNotFound,
    PositionNotFound,

    /// <summary>Bloqueado por BR-20 até existir o módulo `approval`.</summary>
    RequiresApproval,
}
