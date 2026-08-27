using Rivo.Audit.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.UseCases;

/// <summary>
/// Vista de leitura de uma requisição.
///
/// <para>
/// <strong>Não devolve quem decidiu nem em que passo está o processo.</strong>
/// Isso é de `approval`, e lê-se em <c>GET /approval/requests/{id}</c> com o
/// <c>ApprovalRequestId</c> daqui. Duplicá-lo aqui criaria uma segunda versão
/// da verdade sobre a decisão, que é exactamente o que `modules/approval.md`
/// proíbe.
/// </para>
/// </summary>
public sealed record RequisitionView(
    Guid RequisitionId,
    Guid RequestedByEmployeeId,
    Guid? DepartmentId,
    string Justification,
    string Currency,
    decimal EstimatedTotal,
    DateOnly RequestedOn,
    string Status,
    Guid? ApprovalRequestId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ClosedAt,
    string? ClosingReason,
    IReadOnlyList<RequisitionLineView> Lines);

public sealed record RequisitionLineView(
    Guid LineId,
    string Description,
    decimal Quantity,
    decimal EstimatedUnitPrice,
    decimal EstimatedTotal);

/// <param name="Description">O que se pretende comprar.</param>
public sealed record NewRequisitionLine(string Description, decimal Quantity, decimal EstimatedUnitPrice);

internal static class RequisitionViews
{
    internal static RequisitionView ToView(PurchaseRequisition requisicao) =>
        new(
            requisicao.Id,
            requisicao.RequestedByEmployeeId,
            requisicao.DepartmentId,
            requisicao.Justification,
            requisicao.Currency,
            requisicao.EstimatedTotal,
            requisicao.RequestedOn,
            requisicao.Status.ToString(),
            requisicao.ApprovalRequestId,
            requisicao.SubmittedAt,
            requisicao.ClosedAt,
            requisicao.ClosingReason,
            [.. requisicao.Lines.Select(l => new RequisitionLineView(
                l.Id, l.Description, l.Quantity, l.EstimatedUnitPrice, l.EstimatedTotal))]);
}

public sealed class ListRequisitions(IProcurementStore store)
{
    public async Task<IReadOnlyList<RequisitionView>> ExecuteAsync(
        Guid? requestedByEmployeeId,
        string? status,
        CancellationToken cancellationToken)
    {
        RequisitionStatus? estado = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequisitionStatus>(status, ignoreCase: true, out var parsed))
            {
                return [];
            }

            estado = parsed;
        }

        var requisicoes = await store.ListRequisitionsAsync(
            requestedByEmployeeId, estado, cancellationToken);

        return [.. requisicoes.Select(RequisitionViews.ToView)];
    }
}

public sealed class GetRequisition(IProcurementStore store)
{
    public async Task<RequisitionView?> ExecuteAsync(Guid requisitionId, CancellationToken cancellationToken)
    {
        var requisicao = await store.FindRequisitionAsync(requisitionId, cancellationToken);

        return requisicao is null ? null : RequisitionViews.ToView(requisicao);
    }
}

/// <summary>
/// Abre uma requisição em rascunho, já com as suas linhas.
///
/// <para>
/// Nasce em rascunho e não submetida, de propósito: submeter é acto separado,
/// e é o que congela o que se pede. Abrir e submeter na mesma chamada tiraria a
/// hipótese de rever antes de mandar para decisão.
/// </para>
/// </summary>
public sealed class OpenRequisition(
    IProcurementStore store,
    IEmployeeDirectory employees,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<OpenRequisitionResult> ExecuteAsync(
        Guid requestedByEmployeeId,
        Guid? departmentId,
        string justification,
        string currency,
        DateOnly? requestedOn,
        IReadOnlyList<NewRequisitionLine> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var agora = clock.GetUtcNow();

        // O requisitante tem de existir, e existe em `hr` (ADR-010). Sem esta
        // verificação, uma requisição podia nascer com um identificador que não
        // é de ninguém — e `approval` só descobriria ao tentar verificar BR-2.
        var colaborador = await employees.FindAsync(requestedByEmployeeId, agora, cancellationToken);

        if (colaborador is null)
        {
            return OpenRequisitionResult.RequesterNotFound();
        }

        PurchaseRequisition requisicao;

        try
        {
            requisicao = PurchaseRequisition.Open(
                requestedByEmployeeId,

                // Departamento indicado ganha ao do colaborador: há requisições
                // feitas em nome de outro departamento. Sem indicação, o do
                // requisitante é a leitura correcta — e pode ser nulo, porque
                // nem todo o colaborador tem departamento.
                departmentId ?? colaborador.DepartmentId,
                justification,
                currency,
                requestedOn ?? DateOnly.FromDateTime(agora.UtcDateTime));

            foreach (var linha in lines)
            {
                requisicao.AddLine(linha.Description, linha.Quantity, linha.EstimatedUnitPrice);
            }
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return OpenRequisitionResult.Rejected(error.Message);
        }

        await store.AddRequisitionAsync(requisicao, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.RequisitionOpened,
                ProcurementAuditEntityTypes.Requisition,
                requisicao.Id.ToString(),
                context,
                NewValue: $$"""{"requestedBy":"{{requisicao.RequestedByEmployeeId}}","estimatedTotal":{{requisicao.EstimatedTotal}},"currency":"{{requisicao.Currency}}"}"""),
            cancellationToken);

        return OpenRequisitionResult.Success(requisicao.Id, requisicao.EstimatedTotal);
    }
}

public sealed record OpenRequisitionResult(
    OpenRequisitionOutcome Outcome,
    Guid? RequisitionId,
    decimal? EstimatedTotal,
    string? Error)
{
    public static OpenRequisitionResult Success(Guid requisitionId, decimal estimatedTotal) =>
        new(OpenRequisitionOutcome.Opened, requisitionId, estimatedTotal, null);

    public static OpenRequisitionResult RequesterNotFound() =>
        new(OpenRequisitionOutcome.RequesterNotFound, null, null,
            "Colaborador requisitante não encontrado.");

    public static OpenRequisitionResult Rejected(string error) =>
        new(OpenRequisitionOutcome.Rejected, null, null, error);
}

public enum OpenRequisitionOutcome
{
    Opened,
    RequesterNotFound,
    Rejected,
}

/// <summary>
/// Submete a requisição a decisão.
///
/// <para>
/// É aqui que `procurement` encontra `approval`, e o encontro é por
/// <c>IProcurementApprovalSubmission</c> — vocabulário deste módulo, ligado ao
/// motor no composition root.
/// </para>
/// </summary>
public sealed class SubmitRequisition(
    IProcurementStore store,
    IProcurementApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<SubmitRequisitionResult> ExecuteAsync(
        Guid requisitionId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var requisicao = await store.FindRequisitionForUpdateAsync(requisitionId, cancellationToken);

        if (requisicao is null)
        {
            return SubmitRequisitionResult.NotFound();
        }

        // Sem governança não se submete: a requisição ficaria a dizer que
        // espera decisão de um motor que não existe, e ninguém a decidiria.
        if (!approvals.IsAvailable)
        {
            return SubmitRequisitionResult.ApprovalUnavailable();
        }

        if (requisicao.Status is not RequisitionStatus.Draft)
        {
            return SubmitRequisitionResult.Rejected(
                $"Só um rascunho se submete. Esta requisição está em {requisicao.Status}.");
        }

        if (requisicao.Lines.Count == 0)
        {
            return SubmitRequisitionResult.Rejected(
                "Uma requisição sem linhas não diz o que se pretende comprar.");
        }

        var submissao = await approvals.SubmitAsync(
            requisicao.Id,
            requisicao.RequestedByEmployeeId,
            requisicao.DepartmentId,
            requisicao.EstimatedTotal,
            requisicao.Currency,
            $"Requisição interna: {requisicao.Justification}",
            cancellationToken);

        if (!submissao.Submitted)
        {
            // A requisição fica em rascunho. Falhar a submissão não é motivo
            // para perder o que já foi escrito — quem a abriu corrige a
            // configuração da política e volta a tentar.
            return SubmitRequisitionResult.SubmissionFailed(submissao.Reason!);
        }

        requisicao.MarkSubmitted(submissao.RequestId!.Value, clock.GetUtcNow());

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.RequisitionSubmitted,
                ProcurementAuditEntityTypes.Requisition,
                requisicao.Id.ToString(),
                context,
                NewValue: $$"""{"approvalRequest":"{{requisicao.ApprovalRequestId}}","estimatedTotal":{{requisicao.EstimatedTotal}}}"""),
            cancellationToken);

        return SubmitRequisitionResult.Success(requisicao.ApprovalRequestId!.Value);
    }
}

public sealed record SubmitRequisitionResult(
    SubmitRequisitionOutcome Outcome,
    Guid? ApprovalRequestId,
    string? Error)
{
    public static SubmitRequisitionResult Success(Guid approvalRequestId) =>
        new(SubmitRequisitionOutcome.Submitted, approvalRequestId, null);

    public static SubmitRequisitionResult NotFound() =>
        new(SubmitRequisitionOutcome.NotFound, null, "Requisição não encontrada.");

    public static SubmitRequisitionResult ApprovalUnavailable() =>
        new(SubmitRequisitionOutcome.ApprovalUnavailable, null,
            "Não há motor de aprovação ligado neste ambiente. Sem decisão não se compra.");

    public static SubmitRequisitionResult SubmissionFailed(string error) =>
        new(SubmitRequisitionOutcome.SubmissionFailed, null, error);

    public static SubmitRequisitionResult Rejected(string error) =>
        new(SubmitRequisitionOutcome.Rejected, null, error);
}

public enum SubmitRequisitionOutcome
{
    Submitted,
    NotFound,

    /// <summary>Sem motor de governança ligado. Traduz-se em 501.</summary>
    ApprovalUnavailable,

    /// <summary>
    /// O motor recusou a submissão — sem política aplicável, políticas
    /// ambíguas, sem aprovadores. É erro de configuração, e traduz-se em 409.
    /// </summary>
    SubmissionFailed,

    /// <summary>Estado errado da requisição. 409.</summary>
    Rejected,
}

/// <summary>
/// Lê a decisão em `approval` e aplica-a à requisição.
///
/// <para>
/// <strong>`procurement` pergunta; `approval` nunca empurra.</strong> Mesmo
/// padrão de <c>hr</c> nas atribuições de cargo e nos pedidos de férias: o
/// efeito é aplicado pelo módulo dono do dado, e nunca pelo motor de decisão.
/// </para>
///
/// <para>
/// É idempotente por construção: uma requisição que já não está pendente
/// devolve o estado em que está, sem falhar. Quem chama pode chamar outra vez.
/// </para>
/// </summary>
public sealed class ApplyRequisitionDecision(
    IProcurementStore store,
    IProcurementApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<RequisitionDecisionResult> ExecuteAsync(
        Guid requisitionId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var requisicao = await store.FindRequisitionForUpdateAsync(requisitionId, cancellationToken);

        if (requisicao is null)
        {
            return RequisitionDecisionResult.NotFound();
        }

        if (requisicao.Status is not RequisitionStatus.PendingApproval)
        {
            return RequisitionDecisionResult.Settled(requisicao.Status.ToString());
        }

        var estado = await approvals.GetStateAsync(requisicao.ApprovalRequestId!.Value, cancellationToken);

        // `Unknown` fica pendente de propósito: não encontrar o processo pode
        // ser falha momentânea, e transformar isso em recusa fecharia uma
        // requisição legítima sem que ninguém tivesse decidido nada.
        if (estado is ProcurementApprovalState.Pending or ProcurementApprovalState.Unknown)
        {
            return RequisitionDecisionResult.StillPending();
        }

        if (estado is ProcurementApprovalState.Approved)
        {
            requisicao.MarkApproved(clock.GetUtcNow());
        }
        else
        {
            requisicao.MarkRefused("Recusada em aprovação.", clock.GetUtcNow());
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                requisicao.Status is RequisitionStatus.Approved
                    ? ProcurementAuditActions.RequisitionApproved
                    : ProcurementAuditActions.RequisitionRefused,
                ProcurementAuditEntityTypes.Requisition,
                requisicao.Id.ToString(),
                context,
                NewValue: $$"""{"status":"{{requisicao.Status}}","approvalRequest":"{{requisicao.ApprovalRequestId}}"}"""),
            cancellationToken);

        return RequisitionDecisionResult.Applied(requisicao.Status.ToString());
    }
}

public sealed record RequisitionDecisionResult(
    RequisitionDecisionOutcome Outcome,
    string? Status,
    string? Error)
{
    public static RequisitionDecisionResult Applied(string status) =>
        new(RequisitionDecisionOutcome.Applied, status, null);

    public static RequisitionDecisionResult StillPending() =>
        new(RequisitionDecisionOutcome.StillPending, RequisitionStatus.PendingApproval.ToString(), null);

    /// <summary>Já tinha sido decidida antes. Não é erro — é a segunda chamada.</summary>
    public static RequisitionDecisionResult Settled(string status) =>
        new(RequisitionDecisionOutcome.AlreadySettled, status, null);

    public static RequisitionDecisionResult NotFound() =>
        new(RequisitionDecisionOutcome.NotFound, null, "Requisição não encontrada.");
}

public enum RequisitionDecisionOutcome
{
    Applied,
    StillPending,
    AlreadySettled,
    NotFound,
}

/// <summary>
/// Cancela uma requisição. Nunca elimina — BR-14.
/// </summary>
public sealed class CancelRequisition(IProcurementStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelRequisitionResult> ExecuteAsync(
        Guid requisitionId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var requisicao = await store.FindRequisitionForUpdateAsync(requisitionId, cancellationToken);

        if (requisicao is null)
        {
            return CancelRequisitionResult.NotFound();
        }

        try
        {
            requisicao.Cancel(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelRequisitionResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.RequisitionCancelled,
                ProcurementAuditEntityTypes.Requisition,
                requisicao.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{requisicao.ClosingReason}}"}"""),
            cancellationToken);

        return CancelRequisitionResult.Success();
    }
}

public sealed record CancelRequisitionResult(CancelRequisitionOutcome Outcome, string? Error)
{
    public static CancelRequisitionResult Success() => new(CancelRequisitionOutcome.Cancelled, null);

    public static CancelRequisitionResult NotFound() =>
        new(CancelRequisitionOutcome.NotFound, "Requisição não encontrada.");

    public static CancelRequisitionResult Rejected(string error) =>
        new(CancelRequisitionOutcome.Rejected, error);
}

public enum CancelRequisitionOutcome
{
    Cancelled,
    NotFound,
    Rejected,
}
