using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Contracts;
using Rivo.Approval.Domain;
using Rivo.Audit.Contracts;

namespace Rivo.Approval.Application.UseCases;

public sealed class ListApprovalPolicies(IApprovalStore store)
{
    public async Task<IReadOnlyList<PolicyView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var policies = await store.ListPoliciesAsync(cancellationToken);

        return [.. policies.Select(p => new PolicyView(
            p.Id, p.ProcessType, p.DepartmentId, p.MinimumAmount, p.MaximumAmount,
            p.RequiresBudgetCheck, p.IsActive, p.Specificity,
            [.. p.Steps.OrderBy(s => s.Order).Select(s => new PolicyStepView(
                s.Order, s.ApproverPositionId, s.Mode.ToString(), s.SlaHours))]))];
    }
}

/// <param name="Specificity">
/// Quanto maior, mais específica. É o critério de desempate entre políticas que
/// correspondem — expor isto permite ver, antes de submeter, qual vai ganhar.
/// </param>
public sealed record PolicyView(
    Guid PolicyId,
    string ProcessType,
    Guid? DepartmentId,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    bool RequiresBudgetCheck,
    bool IsActive,
    int Specificity,
    IReadOnlyList<PolicyStepView> Steps);

public sealed record PolicyStepView(int Order, Guid ApproverPositionId, string Mode, int? SlaHours);

/// <summary>
/// Cria uma política com os seus passos.
///
/// <para>
/// Os passos vêm no mesmo pedido porque uma política sem passos não aprova
/// nada — e uma submissão que lhe caísse em cima seria recusada com
/// "nenhum aprovador resolvido", que aponta para o sítio errado.
/// </para>
/// </summary>
public sealed class CreateApprovalPolicy(IApprovalStore store, IAuditTrail audit)
{
    public async Task<PolicyResult> ExecuteAsync(
        string processType,
        Guid? departmentId,
        decimal? minimumAmount,
        decimal? maximumAmount,
        bool requiresBudgetCheck,
        IReadOnlyList<NewPolicyStep> steps,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
        {
            return PolicyResult.Rejected("Uma política sem passos não aprova nada.");
        }

        ApprovalPolicy policy;

        try
        {
            policy = ApprovalPolicy.Create(
                processType, departmentId, minimumAmount, maximumAmount, requiresBudgetCheck);

            foreach (var step in steps)
            {
                var mode = Enum.TryParse<StepMode>(step.Mode, ignoreCase: true, out var parsed)
                    ? parsed
                    : throw new ArgumentException(
                        $"Modo desconhecido '{step.Mode}'. Esperado: {string.Join(", ", Enum.GetNames<StepMode>())}.");

                policy.AddStep(step.ApproverPositionId, mode, step.SlaHours);
            }
        }
        catch (ArgumentException error)
        {
            return PolicyResult.Rejected(error.Message);
        }

        await store.AddPolicyAsync(policy, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ApprovalAuditActions.PolicyCreated,
                ApprovalAuditEntityTypes.Policy,
                policy.Id.ToString(),
                context,
                NewValue: $$"""{"processType":"{{policy.ProcessType}}","steps":{{policy.Steps.Count}}}"""),
            cancellationToken);

        return PolicyResult.Success(policy.Id);
    }
}

public sealed record NewPolicyStep(Guid ApproverPositionId, string Mode, int? SlaHours);

public sealed record PolicyResult(bool Succeeded, Guid? PolicyId, string? Error)
{
    public static PolicyResult Success(Guid id) => new(true, id, null);

    public static PolicyResult Rejected(string reason) => new(false, null, reason);
}

public sealed class ListApprovalRequests(IApprovalStore store)
{
    /// <param name="pendingForEmployeeId">
    /// Só os pedidos à espera desta pessoa. É a caixa de entrada de quem
    /// aprova — e a consulta que a fila de RH faz.
    /// </param>
    public async Task<IReadOnlyList<ApprovalStatusView>> ExecuteAsync(
        string? processType,
        Guid? pendingForEmployeeId,
        CancellationToken cancellationToken)
    {
        var requests = await store.ListRequestsAsync(processType, pendingForEmployeeId, cancellationToken);

        return [.. requests.Select(ApprovalGateway.Project)];
    }
}

/// <summary>
/// Regista uma decisão.
///
/// <para>
/// As regras estão no domínio (ADR-008). O que este caso de uso acrescenta é o
/// que o domínio não pode fazer: <strong>auditar a tentativa recusada</strong>.
/// Uma violação de BR-2 ou BR-4 é evento de segurança, e desaparecer sem rasto
/// é precisamente o que não pode acontecer.
/// </para>
/// </summary>
public sealed class DecideOnRequest(IApprovalStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<DecisionResult> ExecuteAsync(
        Guid requestId,
        Guid decidedByEmployeeId,
        string action,
        string? notes,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DecisionAction>(action, ignoreCase: true, out var decision))
        {
            return DecisionResult.Rejected(
                $"Decisão desconhecida. Esperado: {string.Join(", ", Enum.GetNames<DecisionAction>())}.");
        }

        var request = await store.FindRequestAsync(requestId, cancellationToken);

        if (request is null)
        {
            return DecisionResult.NotFound();
        }

        try
        {
            request.Decide(decidedByEmployeeId, decision, clock.GetUtcNow(), notes);
        }
        catch (SegregationOfDutiesException error)
        {
            // A tentativa não produziu efeito, e vai para a trilha na mesma:
            // uma sequência destas contra o mesmo pedido é o padrão que
            // interessa detectar.
            await audit.RecordAsync(
                new AuditRecord(
                    ApprovalAuditActions.SegregationViolationAttempted,
                    ApprovalAuditEntityTypes.Request,
                    requestId.ToString(),
                    context,
                    NewValue: $$"""{"attemptedBy":"{{decidedByEmployeeId}}","reason":"{{error.Message}}"}"""),
                cancellationToken);

            return DecisionResult.SegregationViolation(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return DecisionResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ApprovalAuditActions.DecisionRecorded,
                ApprovalAuditEntityTypes.Request,
                request.Id.ToString(),
                context,
                NewValue: $$"""{"action":"{{decision}}","step":{{request.CurrentStep}},"status":"{{request.Status}}"}"""),
            cancellationToken);

        return DecisionResult.Success(ApprovalGateway.Project(request));
    }
}

public sealed record DecisionResult(DecisionOutcome Outcome, ApprovalStatusView? Status, string? Error)
{
    public static DecisionResult Success(ApprovalStatusView status) =>
        new(DecisionOutcome.Recorded, status, null);

    public static DecisionResult NotFound() =>
        new(DecisionOutcome.NotFound, null, "Pedido não encontrado.");

    public static DecisionResult Rejected(string reason) => new(DecisionOutcome.Rejected, null, reason);

    public static DecisionResult SegregationViolation(string reason) =>
        new(DecisionOutcome.SegregationViolation, null, reason);
}

public enum DecisionOutcome
{
    Recorded,
    NotFound,
    Rejected,

    /// <summary>
    /// BR-2 ou BR-4. Distinto de <see cref="Rejected"/> de propósito: traduz-se
    /// em 403 e não em 409 — não é o estado que impede, é a pessoa.
    /// </summary>
    SegregationViolation,
}

public sealed class CancelRequest(IApprovalStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<DecisionResult> ExecuteAsync(
        Guid requestId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var request = await store.FindRequestAsync(requestId, cancellationToken);

        if (request is null)
        {
            return DecisionResult.NotFound();
        }

        try
        {
            request.Cancel(clock.GetUtcNow());
        }
        catch (InvalidOperationException error)
        {
            return DecisionResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ApprovalAuditActions.RequestCancelled,
                ApprovalAuditEntityTypes.Request,
                request.Id.ToString(),
                context),
            cancellationToken);

        return DecisionResult.Success(ApprovalGateway.Project(request));
    }
}
