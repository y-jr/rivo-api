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

        // Submeter um candidato a um Cargo com autoridade **enquanto outro o
        // ocupa** não é o problema (#39) — é o caso normal de rever quem
        // sucede a quem, e BR-2/BR-20 já impedem que isso confira autoridade
        // sem decisão. O que #39 impede é ficar efectivo duas vezes: ver
        // ApplyPositionApprovalOutcome, que é onde uma pendente é promovida.
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

    /// <summary>
    /// Atribui um Cargo com efeito imediato, ignorando BR-20 mesmo quando o
    /// Cargo confere autoridade de aprovação (ADR-058).
    ///
    /// <para>
    /// <strong>Não é a operação normal.</strong> Só chega aqui quem tem
    /// <see cref="HrPermissions.PositionsAssignDirect"/> — a conta de
    /// operação <c>SuperAdmin</c> — porque é o único caminho para resolver o
    /// arranque circular do motor de aprovação: nenhum Cargo aprovador existe
    /// ainda para decidir a atribuição do primeiro. Audita-se com uma acção
    /// própria para ficar visível na trilha que esta atribuição saltou a
    /// aprovação.
    /// </para>
    /// </summary>
    public async Task<AssignPositionResult> ExecuteDirectAsync(
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

        if (await PositionOccupancy.IsOccupiedAsync(store, position, effectiveFrom, cancellationToken))
        {
            return AssignPositionResult.PositionOccupied();
        }

        var assignment = PositionAssignment.CreateEffective(employeeId, positionId, effectiveFrom, effectiveTo);

        await store.AddAssignmentAsync(assignment, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionAssignedDirectly,
                HrAuditEntityTypes.Employee,
                employeeId.ToString(),
                context,
                NewValue: $$"""{"positionId":"{{positionId}}","position":"{{position.Name}}","status":"Effective","grantsApprovalAuthority":{{(position.GrantsApprovalAuthority ? "true" : "false")}},"bypassedApproval":true}"""),
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

    public static AssignPositionResult PositionOccupied() =>
        new(AssignPositionOutcome.PositionOccupied, null,
            "Este cargo já está ocupado. Encerre a atribuição actual " +
            "(POST /hr/position-assignments/{id}/closure) antes de atribuir outra pessoa.");
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

    /// <summary>
    /// Já há uma atribuição efectiva para este Cargo à data pedida (#39). 409 —
    /// encerra-se a actual antes de atribuir outra pessoa, nunca automaticamente.
    /// </summary>
    PositionOccupied,
}

/// <summary>
/// Encerra a ocupação de um Cargo (#39 do levantamento de pendências).
///
/// <para>
/// Sem isto, nada impedia várias pessoas ocuparem o mesmo Cargo em simultâneo
/// — e é exactamente esse silêncio que <see cref="AssignPosition"/> agora
/// recusa (<see cref="AssignPositionOutcome.PositionOccupied"/>). Gémeo de
/// <c>EndVehicleAssignment</c> em `fleet`, mesmo padrão.
/// </para>
/// </summary>
public sealed class EndPositionAssignment(IHrStore store, IAuditTrail audit)
{
    public async Task<PositionAssignmentClosureResult> ExecuteAsync(
        Guid assignmentId,
        DateTimeOffset endedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var assignment = await store.FindAssignmentAsync(assignmentId, cancellationToken);

        if (assignment is null)
        {
            return PositionAssignmentClosureResult.NotFound();
        }

        try
        {
            assignment.End(endedOn);
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or InvalidOperationException)
        {
            return PositionAssignmentClosureResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionAssignmentEnded,
                HrAuditEntityTypes.Employee,
                assignment.EmployeeId.ToString(),
                context,
                NewValue: $$"""{"assignmentId":"{{assignmentId}}","positionId":"{{assignment.PositionId}}","endedOn":"{{endedOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return PositionAssignmentClosureResult.Success();
    }
}

public sealed record PositionAssignmentClosureResult(PositionAssignmentClosureOutcome Outcome, string? Error)
{
    public static PositionAssignmentClosureResult Success() =>
        new(PositionAssignmentClosureOutcome.Ended, null);

    public static PositionAssignmentClosureResult NotFound() =>
        new(PositionAssignmentClosureOutcome.NotFound, "Atribuição não encontrada.");

    public static PositionAssignmentClosureResult Rejected(string reason) =>
        new(PositionAssignmentClosureOutcome.Rejected, reason);
}

public enum PositionAssignmentClosureOutcome
{
    Ended,
    NotFound,

    /// <summary>Não é efectiva, já tinha terminado, ou a data de fim é anterior ao início. 409.</summary>
    Rejected,
}

/// <summary>
/// Se um Cargo já tem quem o ocupe à data pedida (#39 do levantamento de
/// pendências).
///
/// <para>
/// <strong>Só se aplica a Cargos com autoridade de aprovação.</strong> O caso
/// relatado — três "CEO" ao mesmo tempo — é um problema de governança
/// (BR-20): duas pessoas com autoridade para o mesmo passo tornam ambíguo quem
/// decide. Um Cargo comum (ex. "Contabilista") não tem essa ambiguidade
/// nenhuma, e vários colaboradores já o ocupam em simultâneo de propósito
/// (organogramas normais) — restringir aí seria inventar uma regra de negócio
/// que ninguém pediu.
/// </para>
///
/// <para>
/// <strong>Verificado ao ficar efectivo, nunca ao submeter.</strong> Candidatar
/// alguém a um Cargo já ocupado é o caso normal de rever quem sucede a quem — é
/// por isso que <see cref="AssignPosition.ExecuteAsync"/> não chama isto antes
/// de <c>SubmitForApprovalAsync</c>. Quem chama é <see cref="AssignPosition.ExecuteDirectAsync"/>
/// (efectivo de imediato, sem governança) e <see cref="ApplyPositionApprovalOutcome"/>
/// (o momento em que uma pendente <em>se tornaria</em> efectiva). O
/// encerramento é sempre explícito (<c>POST /hr/position-assignments/{id}/closure</c>),
/// nunca automático.
/// </para>
/// </summary>
internal static class PositionOccupancy
{
    public static async Task<bool> IsOccupiedAsync(
        IHrStore store, Position position, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (!position.GrantsApprovalAuthority)
        {
            return false;
        }

        var existentes = await store.ListAssignmentsForPositionAsync(position.Id, cancellationToken);

        return existentes.Any(assignment => assignment.IsEffectiveAt(asOf));
    }
}

/// <summary>
/// Quem ocupa um Cargo — histórico completo, não só quem o ocupa agora.
///
/// <para>
/// Faltava desde o #39 (levantamento de pendências): o encerramento
/// (<see cref="EndPositionAssignment"/>) existe, mas sem uma leitura que
/// devolva o <c>assignmentId</c>, ninguém — nem o frontend, nem um operador —
/// tinha como descobrir <em>qual</em> atribuição encerrar. Devolve o histórico
/// inteiro, e não só a efectiva, pelo mesmo motivo que
/// <c>ListAssignmentsForEmployeeAsync</c> também o faz: uma atribuição
/// recusada ou já encerrada é facto histórico, não um registo para esconder.
/// </para>
/// </summary>
public sealed class ListPositionAssignments(IHrStore store)
{
    public async Task<IReadOnlyList<PositionAssignmentView>> ExecuteAsync(
        Guid positionId, CancellationToken cancellationToken)
    {
        var atribuicoes = await store.ListAssignmentsForPositionAsync(positionId, cancellationToken);

        return [.. atribuicoes
            .OrderByDescending(a => a.EffectiveFrom)
            .Select(a => new PositionAssignmentView(
                a.Id, a.EmployeeId, a.EffectiveFrom, a.EffectiveTo, a.Status.ToString()))];
    }
}

public sealed record PositionAssignmentView(
    Guid AssignmentId,
    Guid EmployeeId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string Status);
