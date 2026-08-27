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

/// <summary>
/// Desactiva uma política de aprovação.
///
/// <para>
/// <strong>Só desactivar, e não reactivar.</strong> A submissão escolhe a
/// política mais específica que se aplica, e recusa quando duas empatam
/// (ADR-034). Reactivar uma política antiga podia criar esse empate sem que
/// quem reactiva o visse — e a recusa apareceria depois, numa submissão que
/// não tem nada a ver com isso. Quem precisa da política outra vez cria-a, e
/// vê o que está activo ao fazê-lo.
/// </para>
///
/// <para>
/// <strong>Os pedidos em curso não mudam.</strong> Cada um guarda a política
/// que lhe foi aplicada e os aprovadores que dela resultaram, congelados na
/// submissão (BR-6). Desactivar afecta o que vem a seguir, nunca o que já está
/// a decorrer.
/// </para>
///
/// <para>
/// Existe também por uma razão prática: sem rota, as suites de verificação
/// eram obrigadas a limpar-se por SQL directo contra a base de dados.
/// </para>
/// </summary>
/// <summary>
/// A linha do tempo completa de um pedido: submissão, aprovadores congelados
/// por passo, e cada decisão registada.
///
/// <para>
/// <strong>Difere de <c>GetStatusAsync</c> em duas coisas.</strong> Esse
/// devolve só <c>PendingAssignments</c> — quem falta decidir agora, para um
/// cliente que espera pela sua vez. Esta rota devolve <strong>todas</strong> as
/// atribuições, incluídas as já decididas e as de passos futuros: é para quem
/// quer reconstruir o que aconteceu, não para quem quer saber o que falta.
/// A submissão também vem completa aqui — requisitante, valor, moeda — que o
/// outro não expõe.
/// </para>
///
/// <para>
/// Só leitura sobre o que já está gravado: não junta nada que `approval` não
/// tivesse guardado.
/// </para>
/// </summary>
public sealed class GetApprovalRequestHistory(IApprovalStore store)
{
    public async Task<ApprovalHistoryView?> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var pedido = await store.FindRequestAsync(requestId, cancellationToken);

        return pedido is null ? null : ToView(pedido);
    }

    private static ApprovalHistoryView ToView(ApprovalRequest pedido) =>
        new(
            pedido.Id,
            pedido.ProcessType,
            pedido.SourceModule,
            pedido.SourceReference,
            pedido.RequestedByEmployeeId,
            pedido.Amount,
            pedido.Currency,
            pedido.DepartmentId,
            pedido.Status.ToString(),
            pedido.SubmittedAt,
            pedido.ClosedAt,
            pedido.CurrentStep,
            pedido.TotalSteps,
            [.. pedido.Assignments
                .OrderBy(a => a.Step)
                .Select(a => new ApprovalAssignmentView(
                    a.Step, a.Mode.ToString(), a.ApproverEmployeeId, a.SlaHours))],
            [.. pedido.Decisions
                .OrderBy(d => d.DecidedAt)
                .Select(d => new ApprovalDecisionView(
                    d.DecidedByEmployeeId, d.Action.ToString(), d.DecidedAt, d.Step, d.Notes))]);
}

/// <param name="Assignments">
/// Todos os aprovadores congelados na submissão, por passo — não só os que
/// ainda faltam decidir (BR-6, BR-19).
/// </param>
/// <param name="Decisions">Por ordem em que foram tomadas.</param>
public sealed record ApprovalHistoryView(
    Guid RequestId,
    string ProcessType,
    string SourceModule,
    string SourceReference,
    Guid RequestedByEmployeeId,
    decimal? Amount,
    string? Currency,
    Guid? DepartmentId,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ClosedAt,
    int CurrentStep,
    int TotalSteps,
    IReadOnlyList<ApprovalAssignmentView> Assignments,
    IReadOnlyList<ApprovalDecisionView> Decisions);

public sealed record ApprovalAssignmentView(int Step, string Mode, Guid ApproverEmployeeId, int? SlaHours);


public sealed class DeactivateApprovalPolicy(IApprovalStore store, IAuditTrail audit)
{
    public async Task<DeactivatePolicyOutcome> ExecuteAsync(
        Guid policyId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var politica = await store.FindPolicyAsync(policyId, cancellationToken);

        if (politica is null)
        {
            return DeactivatePolicyOutcome.NotFound;
        }

        // Repetível sem erro: desactivar uma política já desactivada produz o
        // estado pretendido na mesma. Sai antes de gravar e de auditar, para a
        // trilha não encher de desactivações que não mudaram nada.
        if (!politica.IsActive)
        {
            return DeactivatePolicyOutcome.AlreadyInactive;
        }

        politica.Deactivate();
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ApprovalAuditActions.PolicyDeactivated,
                ApprovalAuditEntityTypes.Policy,
                politica.Id.ToString(),
                context,
                PreviousValue: $$"""{"processType":"{{politica.ProcessType}}","active":true}"""),
            cancellationToken);

        return DeactivatePolicyOutcome.Deactivated;
    }
}

public enum DeactivatePolicyOutcome
{
    Deactivated,

    /// <summary>Já estava desactivada. Não é erro — 204 na mesma.</summary>
    AlreadyInactive,

    NotFound,
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
