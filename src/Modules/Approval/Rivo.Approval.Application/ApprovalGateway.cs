using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Contracts;
using Rivo.Approval.Domain;
using Rivo.Audit.Contracts;
using Rivo.Finance.Contracts;
using Rivo.Hr.Contracts;

namespace Rivo.Approval.Application;

/// <summary>
/// Implementação do contrato publicado por `approval` (ADR-034).
///
/// <para>
/// Vive em Application, tal como <c>EmployeeDirectory</c> em `hr`: é
/// orquestração, e não infraestrutura.
/// </para>
///
/// <para>
/// <strong>É aqui que a submissão congela o processo</strong> — escolhe a
/// política, resolve os Cargos em pessoas concretas e entrega tudo isso ao
/// domínio, que a partir daí não volta a consultar nada de fora (BR-6).
/// </para>
/// </summary>
/// <param name="budgets">
/// Fonte do disponível orçamental (BR-8). <strong>Anulável de propósito:</strong>
/// um ambiente sem `finance` ligado continua a arrancar — e uma política que
/// exija verificação orçamental é aí recusada, não aprovada às cegas.
/// </param>
public sealed class ApprovalGateway(
    IApprovalStore store,
    IEmployeeDirectory employees,
    IAuditTrail audit,
    TimeProvider clock,
    IBudgetAvailability? budgets = null) : IApprovalGateway
{
    public async Task<SubmissionResult> SubmitAsync(
        ApprovalSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var candidates = await store.ListPoliciesForProcessAsync(submission.ProcessType, cancellationToken);

        var matching = candidates
            .Where(p => p.Matches(submission.ProcessType, submission.DepartmentId, submission.Amount))
            .ToList();

        if (matching.Count == 0)
        {
            return SubmissionResult.NoPolicy(
                $"Nenhuma política de aprovação corresponde a '{submission.ProcessType}'. " +
                "É configuração em falta, não um problema do pedido.");
        }

        var bestSpecificity = matching.Max(p => p.Specificity);
        var best = matching.Where(p => p.Specificity == bestSpecificity).ToList();

        // Empate recusa-se em vez de se escolher uma ao acaso: duas políticas
        // igualmente aplicáveis significam que ninguém sabe qual é a alçada
        // (ADR-034).
        if (best.Count > 1)
        {
            return SubmissionResult.AmbiguousPolicy(
                $"{best.Count} políticas igualmente específicas correspondem a este pedido. " +
                "Corrija a configuração — o sistema não escolhe por si.");
        }

        var policy = best[0];

        var now = clock.GetUtcNow();

        // **BR-8, e é aqui que ela acontece.**
        //
        // Antes da decisão, não depois: o ponto de RN-017 é que ninguém decide
        // sobre uma despesa que já se sabe não caber. Verificar depois seria
        // pedir a alguém que aprovasse e só então descobrir que não podia.
        //
        // A verificação corre **antes de resolver aprovadores** porque é a mais
        // barata de falhar: um processo que não passa no orçamento não chega a
        // criar-se, e não fica nada por limpar.
        if (policy.RequiresBudgetCheck)
        {
            if (budgets is null)
            {
                return SubmissionResult.BudgetCheckUnavailable(
                    "Esta política exige verificação orçamental (BR-8) e não há fonte de " +
                    "orçamento ligada neste ambiente. O pedido não foi criado.");
            }

            if (submission.Amount is not { } valor || string.IsNullOrWhiteSpace(submission.Currency))
            {
                return SubmissionResult.BudgetCheckUnavailable(
                    "Esta política exige verificação orçamental (BR-8) e o processo não traz " +
                    "valor. Um processo sem valor não consome orçamento — e a política não " +
                    "devia exigir a verificação.");
            }

            var check = await budgets.CheckAsync(
                new BudgetCheck(
                    submission.BudgetReference,
                    submission.DepartmentId,
                    valor,
                    submission.Currency,
                    DateOnly.FromDateTime(now.UtcDateTime)),
                cancellationToken);

            // **Só `Within` deixa passar.** Todos os outros recusam, incluindo
            // os que significam "não consegui verificar": uma política que
            // exige verificação está a dizer que não se decide sem saber, e
            // aprovar por omissão é o modo de falha que BR-8 existe para
            // impedir.
            if (check.Outcome is not BudgetCheckOutcome.Within)
            {
                return check.Outcome is BudgetCheckOutcome.Exceeded
                    ? SubmissionResult.BudgetExceeded(check.Reason!)
                    : SubmissionResult.BudgetCheckUnavailable(check.Reason!);
            }
        }

        var resolved = new List<ResolvedStep>();

        foreach (var step in policy.Steps.OrderBy(s => s.Order))
        {
            // A resolução é à data da submissão — é isto que BR-6 quer dizer
            // por "congelado": quem ocupa o cargo *agora*, e não quem vier a
            // ocupá-lo.
            var occupants = await employees.FindByPositionAsync(step.ApproverPositionId, now, cancellationToken);

            resolved.Add(new ResolvedStep(
                step.Order,
                step.Mode,
                [.. occupants.Where(o => o.Status == EmployeeStatus.Active).Select(o => o.EmployeeId)],
                step.SlaHours));
        }

        if (resolved.All(s => s.ApproverEmployeeIds.Count == 0))
        {
            return SubmissionResult.NoApprovers(
                "Nenhum dos cargos desta política tem ocupante activo. " +
                "O processo ficaria pendente para sempre.");
        }

        ApprovalRequest request;

        try
        {
            request = ApprovalRequest.Submit(
                submission.ProcessType,
                submission.SourceModule,
                submission.SourceReference,
                submission.RequestedByEmployeeId,
                submission.Amount,
                submission.Currency,
                submission.DepartmentId,
                policy,
                resolved,
                now,
                submission.Summary);
        }
        catch (ArgumentException error)
        {
            return SubmissionResult.NoApprovers(error.Message);
        }

        await store.AddRequestAsync(request, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ApprovalAuditActions.RequestSubmitted,
                ApprovalAuditEntityTypes.Request,
                request.Id.ToString(),
                new AuditContext(null, null, null),
                NewValue: $$"""{"processType":"{{request.ProcessType}}","source":"{{request.SourceModule}}:{{request.SourceReference}}"}"""),
            cancellationToken);

        return SubmissionResult.Submitted(request.Id);
    }

    public async Task<ApprovalStatusView?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await store.FindRequestAsync(requestId, cancellationToken);

        return request is null ? null : Project(request);
    }

    internal static ApprovalStatusView Project(ApprovalRequest r) =>
        new(r.Id,
            r.ProcessType,
            r.SourceModule,
            r.SourceReference,
            r.Status.ToString(),
            r.CurrentStep,
            r.TotalSteps,
            [.. r.PendingAssignments.Select(a => a.ApproverEmployeeId)],
            [.. r.Decisions.Select(d => new ApprovalDecisionView(
                d.DecidedByEmployeeId, d.Action.ToString(), d.DecidedAt, d.Step, d.Notes))]);
}

/// <summary>
/// Acções auditadas por `approval`. Uma tentativa de violar segregação tem
/// acção própria: é evento de segurança, não um erro qualquer.
/// </summary>
public static class ApprovalAuditActions
{
    public const string RequestSubmitted = "approval.request.submitted";
    public const string DecisionRecorded = "approval.request.decided";
    public const string RequestCancelled = "approval.request.cancelled";
    public const string PolicyCreated = "approval.policy.created";

    /// <summary>
    /// Tentativa recusada por BR-2 ou BR-4. Fica na trilha mesmo não tendo
    /// produzido efeito — é precisamente o padrão que interessa detectar.
    /// </summary>
    public const string SegregationViolationAttempted = "approval.request.segregation_violation";
}

public static class ApprovalAuditEntityTypes
{
    public const string Request = "approval.request";
    public const string Policy = "approval.policy";
}
