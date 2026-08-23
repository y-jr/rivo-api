using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Aplica a uma atribuição pendente a decisão já tomada em governança.
///
/// <para>
/// <strong>É `hr` que pergunta, e nunca o contrário.</strong>
/// `modules/approval.md` proíbe expressamente que `approval` modifique dados de
/// negócio do módulo de origem — o que significa que a promoção a efectiva tem
/// de partir daqui. Sem isto, uma atribuição aprovada ficaria pendente para
/// sempre.
/// </para>
///
/// <para>
/// <strong>Idempotente.</strong> Chamar duas vezes sobre a mesma atribuição não
/// faz mal: a segunda encontra-a já resolvida e diz isso. É o que permite
/// chamá-la sem coordenação — do frontend depois de decidir, ou em lote.
/// </para>
/// </summary>
public sealed class ApplyPositionApprovalOutcome(
    IHrStore store,
    IPositionApprovalSubmission approvals,
    IAuditTrail audit)
{
    public async Task<ApplyApprovalResult> ExecuteAsync(
        Guid assignmentId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var assignment = await store.FindAssignmentAsync(assignmentId, cancellationToken);

        if (assignment is null)
        {
            return ApplyApprovalResult.NotFound("Atribuição não encontrada.");
        }

        if (assignment.Status != PositionAssignmentStatus.Pending)
        {
            // Já resolvida. Não é erro — é a idempotência a funcionar.
            return ApplyApprovalResult.AlreadyResolved(assignment.Status.ToString());
        }

        if (assignment.ApprovalRequestId is not { } requestId)
        {
            // Pendente sem processo: não devia existir, e promovê-la seria
            // conferir autoridade sem ninguém a ter aprovado.
            return ApplyApprovalResult.NotFound(
                "Atribuição pendente sem processo de aprovação associado.");
        }

        var state = await approvals.GetStateAsync(requestId, cancellationToken);

        switch (state)
        {
            case PositionApprovalState.Approved:
                assignment.MakeEffective();
                break;

            case PositionApprovalState.Refused:
                assignment.RejectByApproval();
                break;

            // Em curso ou desconhecido: **não se toca**. Promover por omissão é
            // exactamente a escalada que BR-20 fecha.
            default:
                return ApplyApprovalResult.StillPending();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                state == PositionApprovalState.Approved
                    ? HrAuditActions.PositionAssignmentApproved
                    : HrAuditActions.PositionAssignmentRefused,
                HrAuditEntityTypes.Employee,
                assignment.EmployeeId.ToString(),
                context,
                NewValue: $$"""{"assignmentId":"{{assignment.Id}}","status":"{{assignment.Status}}","approvalRequestId":"{{requestId}}"}"""),
            cancellationToken);

        return ApplyApprovalResult.Applied(assignment.Status.ToString());
    }
}

public sealed record ApplyApprovalResult(ApplyApprovalOutcome Outcome, string? Status, string? Message)
{
    public static ApplyApprovalResult Applied(string status) =>
        new(ApplyApprovalOutcome.Applied, status, null);

    public static ApplyApprovalResult AlreadyResolved(string status) =>
        new(ApplyApprovalOutcome.AlreadyResolved, status, "A atribuição já tinha sido resolvida.");

    public static ApplyApprovalResult StillPending() =>
        new(ApplyApprovalOutcome.StillPending, "Pending", "O processo ainda não foi decidido.");

    public static ApplyApprovalResult NotFound(string reason) =>
        new(ApplyApprovalOutcome.NotFound, null, reason);
}

public enum ApplyApprovalOutcome
{
    Applied,
    AlreadyResolved,
    StillPending,
    NotFound,
}
