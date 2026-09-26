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
    IHrApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
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
            case HrApprovalState.Approved:
                // #39: é aqui, e não na submissão, que duas pessoas ficariam
                // efectivas no mesmo Cargo com autoridade — candidatar alguém
                // enquanto outro o ocupa é normal (rever quem sucede a quem);
                // promovê-la por cima de quem já lá está é que não. A pendente
                // fica pendente: quem decidir encerra a actual primeiro
                // (POST /hr/position-assignments/{id}/closure) e chama isto
                // outra vez — idempotente, como já é.
                //
                // "Agora" (não assignment.EffectiveFrom, a data pedida na
                // submissão): o que interessa é se alguém ocupa o Cargo no
                // momento em que esta promoção aconteceria, não se alguém o
                // ocupava quando o candidato foi submetido — senão encerrar o
                // antigo *depois* de submeter o novo nunca libertaria nada,
                // porque o antigo continuaria "efectivo" à data (mais antiga)
                // da submissão.
                var position = await store.FindPositionAsync(assignment.PositionId, cancellationToken);

                if (position is not null
                    && await PositionOccupancy.IsOccupiedAsync(store, position, clock.GetUtcNow(), cancellationToken))
                {
                    return ApplyApprovalResult.Blocked();
                }

                assignment.MakeEffective();
                break;

            case HrApprovalState.Refused:
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
                state == HrApprovalState.Approved
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

    /// <summary>Aprovada, mas o Cargo já está ocupado (#39). Continua Pending.</summary>
    public static ApplyApprovalResult Blocked() =>
        new(ApplyApprovalOutcome.Blocked, "Pending",
            "Aprovado em approval, mas o cargo já está ocupado. Encerre a atribuição actual " +
            "(POST /hr/position-assignments/{id}/closure) antes de aplicar esta decisão.");
}

public enum ApplyApprovalOutcome
{
    Applied,
    AlreadyResolved,
    StillPending,
    NotFound,

    /// <summary>
    /// Aprovada em `approval`, mas o Cargo já tem quem o ocupe (#39) — nunca se
    /// promove por cima de quem já lá está. 409.
    /// </summary>
    Blocked,
}

/// <summary>
/// Varre as atribuições à espera de decisão e aplica as que já foram decididas.
///
/// <para>
/// <strong>É o que fecha o ciclo sem ninguém carregar num botão.</strong>
/// `approval` não pode empurrar a decisão — `modules/approval.md` proíbe-lhe
/// modificar dados de negócio do módulo de origem — por isso alguém tem de
/// perguntar. Sem isto, uma atribuição aprovada ficava pendente até um humano
/// se lembrar.
/// </para>
///
/// <para>
/// Sondagem e não evento: é o mesmo padrão do worker de entrega de
/// `notifications`, e pela mesma razão — não há barramento de eventos, e a
/// tabela já é a fila.
/// </para>
/// </summary>
public sealed class ReconcilePendingAssignments(
    IHrStore store,
    ApplyPositionApprovalOutcome apply,
    IHrApprovalSubmission approvals)
{
    public async Task<ReconciliationOutcome> ExecuteAsync(int batchSize, CancellationToken cancellationToken)
    {
        // Sem motor de governança ligado não há a quem perguntar. Sai em
        // silêncio em vez de acumular erros num ciclo que corre para sempre.
        if (!approvals.IsAvailable)
        {
            return ReconciliationOutcome.Empty;
        }

        var pendentes = await store.ListAssignmentsAwaitingDecisionAsync(batchSize, cancellationToken);

        var aplicadas = 0;
        var porDecidir = 0;
        var falhadas = 0;

        foreach (var assignmentId in pendentes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Actor nulo: é processo automático, e o contrato de auditoria
                // prevê-o explicitamente. Uma promoção feita pelo worker tem de
                // ser distinguível de uma feita por uma pessoa.
                var resultado = await apply.ExecuteAsync(
                    assignmentId, new AuditContext(null, null, null), cancellationToken);

                switch (resultado.Outcome)
                {
                    case ApplyApprovalOutcome.Applied:
                        aplicadas++;
                        break;

                    case ApplyApprovalOutcome.StillPending:
                        porDecidir++;
                        break;

                    default:
                        break;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Uma atribuição que falhe não pode levar o lote atrás. Fica
                // pendente e volta no ciclo seguinte.
                falhadas++;
            }
        }

        return new ReconciliationOutcome(pendentes.Count, aplicadas, porDecidir, falhadas);
    }
}

/// <param name="Examined">Quantas foram consultadas neste ciclo.</param>
/// <param name="Applied">Decididas e aplicadas — efectivas ou recusadas.</param>
/// <param name="StillPending">Continuam à espera de decisão.</param>
public sealed record ReconciliationOutcome(int Examined, int Applied, int StillPending, int Failed)
{
    public static readonly ReconciliationOutcome Empty = new(0, 0, 0, 0);
}
