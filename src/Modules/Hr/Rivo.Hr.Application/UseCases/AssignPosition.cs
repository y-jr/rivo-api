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
public sealed class AssignPosition(
    IHrStore store,
    IAuditTrail audit,
    IHrApprovalSubmission approvals)
{
    /// <summary>
    /// Caminho de um Cargo que confere autoridade de aprovação (BR-20,
    /// ADR-015).
    ///
    /// <para>
    /// A atribuição é criada <strong>pendente</strong> e submetida a decisão.
    /// Pendente não confere Cargo nenhum — <c>IsEffectiveAt</c> só reconhece as
    /// efectivas, e é isso que mantém fechado o caminho de escalada: quem
    /// atribui não passa a decidir quem aprova.
    /// </para>
    ///
    /// <para>
    /// A gravação vem <strong>depois</strong> da submissão bem sucedida. Ao
    /// contrário, uma submissão falhada deixaria uma atribuição pendente sem
    /// processo que a decidisse — pendente para sempre, e invisível.
    /// </para>
    /// </summary>
    private async Task<AssignPositionResult> SubmitForApprovalAsync(
        Guid employeeId,
        Guid? departmentId,
        Position position,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Sem motor de governança ligado, volta-se à recusa: melhor não
        // atribuir do que atribuir autoridade sem quem a aprove.
        if (!approvals.IsAvailable)
        {
            return AssignPositionResult.ApprovalUnavailable(position.Name);
        }

        var assignment = PositionAssignment.CreatePending(
            employeeId, position.Id, effectiveFrom, effectiveTo);

        var submission = await approvals.SubmitAsync(
            HrApprovalProcess.PositionAssignment,
            assignment.Id,
            employeeId,
            departmentId,
            $"Atribuição do cargo '{position.Name}', que confere autoridade de aprovação.",
            cancellationToken);

        if (!submission.Submitted)
        {
            return AssignPositionResult.ApprovalRefusedSubmission(submission.Reason!);
        }

        assignment.LinkToApprovalRequest(submission.RequestId!.Value);

        await store.AddAssignmentAsync(assignment, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionAssignmentSubmitted,
                HrAuditEntityTypes.Employee,
                employeeId.ToString(),
                context,
                NewValue: $$"""{"positionId":"{{position.Id}}","position":"{{position.Name}}","status":"Pending","approvalRequestId":"{{submission.RequestId}}"}"""),
            cancellationToken);

        return AssignPositionResult.PendingApproval(assignment.Id, submission.RequestId.Value);
    }

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
            return await SubmitForApprovalAsync(
                employeeId, employee.DepartmentId, position, effectiveFrom, effectiveTo, context, cancellationToken);
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

    /// <summary>
    /// Submetida e à espera de decisão. <strong>Não confere o Cargo</strong> —
    /// e é essa a diferença entre isto e <see cref="Assigned"/>.
    /// </summary>
    public static AssignPositionResult PendingApproval(Guid assignmentId, Guid requestId) =>
        new(AssignPositionOutcome.PendingApproval, assignmentId,
            $"Atribuição submetida a aprovação (BR-20). Processo {requestId}. " +
            "Só produz efeito depois de aprovada.");

    public static AssignPositionResult ApprovalUnavailable(string positionName) =>
        new(
            AssignPositionOutcome.ApprovalUnavailable,
            null,
            $"O cargo '{positionName}' confere autoridade de aprovação, pelo que a atribuição " +
            "tem de ser aprovada (BR-20). Não há motor de governança ligado neste ambiente.");

    public static AssignPositionResult ApprovalRefusedSubmission(string reason) =>
        new(AssignPositionOutcome.ApprovalRefusedSubmission, null, reason);
}

public enum AssignPositionOutcome
{
    Assigned,
    EmployeeNotFound,
    PositionNotFound,

    /// <summary>Submetida a `approval`. Pendente não confere autoridade (BR-20).</summary>
    PendingApproval,

    /// <summary>Sem motor de governança ligado. Recusa-se, como antes do ADR-034.</summary>
    ApprovalUnavailable,

    /// <summary>
    /// A governança recusou receber o processo — tipicamente política em falta
    /// ou ambígua, ou nenhum cargo da política com ocupante.
    /// </summary>
    ApprovalRefusedSubmission,
}
