using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

public sealed class ListLeave(IHrStore store)
{
    public async Task<IReadOnlyList<LeaveView>> ExecuteAsync(
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var pedidos = await store.ListLeaveAsync(employeeId, cancellationToken);

        return [.. pedidos.Select(l => new LeaveView(
            l.Id, l.EmployeeId, l.Type.ToString(), l.StartsOn, l.EndsOn,
            l.CalendarDays, l.Status.ToString(), l.Reason, l.ApprovalRequestId))];
    }
}

/// <param name="CalendarDays">
/// Dias de calendário, extremos incluídos. <strong>Não são dias úteis</strong> —
/// descontar feriados exigiria um calendário de Angola que o sistema não tem.
/// </param>
public sealed record LeaveView(
    Guid LeaveId,
    Guid EmployeeId,
    string Type,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int CalendarDays,
    string Status,
    string? Reason,
    Guid? ApprovalRequestId);

/// <summary>
/// Submete um pedido de férias a governança.
///
/// <para>
/// Segue o padrão genérico de submissão de `docs` §1(d), o mesmo da atribuição
/// de Cargo: o módulo de origem cria o pedido de negócio, submete-o, e só
/// produz o efeito quando houver decisão. <strong>`hr` nunca tem passos de
/// aprovação próprios</strong> — `modules/hr.md` proíbe-o expressamente, e é a
/// correcção ao anti-padrão do protótipo.
/// </para>
///
/// <para>
/// <strong>Não verifica saldo de férias.</strong> As regras de acumulação e
/// carry-over não estão detalhadas em `docs` (`modules/hr.md`, perguntas em
/// aberto), e um contador construído por suposição daria um número errado com
/// ar de certo.
/// </para>
/// </summary>
public sealed class RequestLeave(
    IHrStore store,
    IAuditTrail audit,
    IHrApprovalSubmission approvals)
{
    public static readonly IReadOnlyList<string> Types = [.. Enum.GetNames<LeaveType>()];

    public async Task<LeaveResult> ExecuteAsync(
        Guid employeeId,
        string type,
        DateOnly startsOn,
        DateOnly endsOn,
        string? reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LeaveType>(type, ignoreCase: true, out var leaveType))
        {
            return LeaveResult.Rejected(
                $"Tipo de ausência desconhecido. Esperado: {string.Join(", ", Types)}.");
        }

        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return LeaveResult.NotFound("Colaborador não encontrado.");
        }

        // Sem governança não se aceita o pedido: ficaria pendente para sempre,
        // e um pedido que nunca é decidido é pior do que um pedido recusado.
        if (!approvals.IsAvailable)
        {
            return LeaveResult.ApprovalUnavailable();
        }

        var existentes = await store.ListLeaveAsync(employeeId, cancellationToken);

        if (existentes.Any(l => l.OverlapsWith(startsOn, endsOn)))
        {
            return LeaveResult.Overlaps();
        }

        LeaveRequest pedido;

        try
        {
            pedido = LeaveRequest.Draft(employeeId, leaveType, startsOn, endsOn, reason);
        }
        catch (ArgumentException error)
        {
            return LeaveResult.Rejected(error.Message);
        }

        // A submissão vem antes da gravação: ao contrário, uma submissão
        // falhada deixaria um pedido pendente sem processo que o decidisse.
        var submissao = await approvals.SubmitAsync(
            HrApprovalProcess.LeaveRequest,
            pedido.Id,
            employeeId,
            employee.DepartmentId,
            $"{pedido.CalendarDays} dia(s) de {leaveType}, de {startsOn:yyyy-MM-dd} a {endsOn:yyyy-MM-dd}.",
            cancellationToken);

        if (!submissao.Submitted)
        {
            return LeaveResult.ApprovalRefusedSubmission(submissao.Reason!);
        }

        pedido.LinkToApprovalRequest(submissao.RequestId!.Value);

        await store.AddLeaveAsync(pedido, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.LeaveRequested,
                HrAuditEntityTypes.LeaveRequest,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{employeeId}}","type":"{{leaveType}}","days":{{pedido.CalendarDays}},"approvalRequestId":"{{submissao.RequestId}}"}"""),
            cancellationToken);

        return LeaveResult.Submitted(pedido.Id, submissao.RequestId.Value);
    }
}

/// <summary>
/// Retira um pedido antes de haver decisão.
/// </summary>
public sealed class CancelLeave(IHrStore store, IAuditTrail audit)
{
    public async Task<LeaveResult> ExecuteAsync(
        Guid leaveId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindLeaveAsync(leaveId, cancellationToken);

        if (pedido is null)
        {
            return LeaveResult.NotFound("Pedido de férias não encontrado.");
        }

        try
        {
            pedido.Cancel();
        }
        catch (InvalidOperationException error)
        {
            return LeaveResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        // O processo em `approval` fica por cancelar: quem o cancela é quem
        // tem autoridade sobre ele, e `hr` não a tem. Fica decidido lá, sem
        // efeito nenhum aqui — o pedido já está retirado.
        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.LeaveCancelled,
                HrAuditEntityTypes.LeaveRequest,
                pedido.Id.ToString(),
                context),
            cancellationToken);

        return LeaveResult.Cancelled(pedido.Id);
    }
}

public sealed record LeaveResult(LeaveOutcome Outcome, Guid? LeaveId, Guid? ApprovalRequestId, string? Message)
{
    public static LeaveResult Submitted(Guid leaveId, Guid requestId) =>
        new(LeaveOutcome.Submitted, leaveId, requestId,
            $"Pedido submetido a aprovação. Processo {requestId}. Só é ausência depois de aprovado.");

    public static LeaveResult Cancelled(Guid leaveId) => new(LeaveOutcome.Cancelled, leaveId, null, null);

    public static LeaveResult NotFound(string reason) => new(LeaveOutcome.NotFound, null, null, reason);

    public static LeaveResult Rejected(string reason) => new(LeaveOutcome.Rejected, null, null, reason);

    public static LeaveResult Overlaps() =>
        new(LeaveOutcome.Overlaps, null, null,
            "O colaborador já tem uma ausência pedida ou aprovada nesse período.");

    public static LeaveResult ApprovalUnavailable() =>
        new(LeaveOutcome.ApprovalUnavailable, null, null,
            "Não há motor de governança ligado neste ambiente, e um pedido de férias " +
            "tem de ser aprovado antes de valer como ausência.");

    public static LeaveResult ApprovalRefusedSubmission(string reason) =>
        new(LeaveOutcome.ApprovalRefusedSubmission, null, null, reason);
}

public enum LeaveOutcome
{
    Submitted,
    Cancelled,
    NotFound,
    Rejected,
    Overlaps,
    ApprovalUnavailable,
    ApprovalRefusedSubmission,
}

/// <summary>
/// Aplica a um pedido pendente a decisão já tomada em governança.
///
/// <para>
/// Gémeo de <see cref="ApplyPositionApprovalOutcome"/>, e pela mesma razão:
/// `approval` não pode modificar dados de negócio do módulo de origem, por isso
/// o efeito parte daqui. Idempotente.
/// </para>
/// </summary>
public sealed class ApplyLeaveApprovalOutcome(
    IHrStore store,
    IHrApprovalSubmission approvals,
    IAuditTrail audit)
{
    public async Task<ApplyApprovalResult> ExecuteAsync(
        Guid leaveId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindLeaveAsync(leaveId, cancellationToken);

        if (pedido is null)
        {
            return ApplyApprovalResult.NotFound("Pedido de férias não encontrado.");
        }

        if (pedido.Status != LeaveStatus.Pending)
        {
            return ApplyApprovalResult.AlreadyResolved(pedido.Status.ToString());
        }

        if (pedido.ApprovalRequestId is not { } requestId)
        {
            return ApplyApprovalResult.NotFound(
                "Pedido pendente sem processo de aprovação associado.");
        }

        var estado = await approvals.GetStateAsync(requestId, cancellationToken);

        switch (estado)
        {
            case HrApprovalState.Approved:
                pedido.Approve();
                break;

            case HrApprovalState.Refused:
                pedido.Refuse();
                break;

            // Em curso ou desconhecido: não se toca. Aprovar por omissão daria
            // ausência sem ninguém a ter decidido.
            default:
                return ApplyApprovalResult.StillPending();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                estado == HrApprovalState.Approved
                    ? HrAuditActions.LeaveApproved
                    : HrAuditActions.LeaveRefused,
                HrAuditEntityTypes.LeaveRequest,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{pedido.EmployeeId}}","status":"{{pedido.Status}}","approvalRequestId":"{{requestId}}"}"""),
            cancellationToken);

        return ApplyApprovalResult.Applied(pedido.Status.ToString());
    }
}

/// <summary>
/// Varre os pedidos de férias à espera de decisão e aplica os já decididos.
/// </summary>
public sealed class ReconcilePendingLeave(
    IHrStore store,
    ApplyLeaveApprovalOutcome apply,
    IHrApprovalSubmission approvals)
{
    public async Task<ReconciliationOutcome> ExecuteAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!approvals.IsAvailable)
        {
            return ReconciliationOutcome.Empty;
        }

        var pendentes = await store.ListLeaveAwaitingDecisionAsync(batchSize, cancellationToken);

        var aplicados = 0;
        var porDecidir = 0;
        var falhados = 0;

        foreach (var leaveId in pendentes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Actor nulo: processo automático, como o contrato de auditoria
                // prevê. Uma aprovação aplicada pelo worker tem de ser
                // distinguível de uma aplicada por uma pessoa.
                var resultado = await apply.ExecuteAsync(
                    leaveId, new AuditContext(null, null, null), cancellationToken);

                switch (resultado.Outcome)
                {
                    case ApplyApprovalOutcome.Applied:
                        aplicados++;
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
                falhados++;
            }
        }

        return new ReconciliationOutcome(pendentes.Count, aplicados, porDecidir, falhados);
    }
}
