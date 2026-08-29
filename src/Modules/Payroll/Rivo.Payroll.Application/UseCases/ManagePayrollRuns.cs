using Rivo.Audit.Contracts;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Application.UseCases;

public sealed class ListPayrollRuns(IPayrollRunStore store)
{
    public async Task<IReadOnlyList<PayrollRun>> ExecuteAsync(CancellationToken cancellationToken) =>
        await store.ListAsync(cancellationToken);
}

public sealed class GetPayrollRun(IPayrollRunStore store)
{
    public Task<PayrollRun?> ExecuteAsync(Guid runId, CancellationToken cancellationToken) =>
        store.FindAsync(runId, cancellationToken);
}

public sealed class OpenPayrollRun(IPayrollRunStore store, IAuditTrail audit)
{
    public async Task<OpenRunResult> ExecuteAsync(
        int year,
        int month,
        Guid openedByEmployeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        PayrollRun folha;

        try
        {
            folha = PayrollRun.Open(year, month, openedByEmployeeId);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return OpenRunResult.Rejected(error.Message);
        }

        await store.AddAsync(folha, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                PayrollAuditActions.RunOpened,
                PayrollAuditEntityTypes.Run,
                folha.Id.ToString(),
                context,
                NewValue: $$"""{"year":{{folha.Year}},"month":{{folha.Month}}}"""),
            cancellationToken);

        return OpenRunResult.Success(folha.Id);
    }
}

public sealed record OpenRunResult(bool Succeeded, Guid? RunId, string? Error)
{
    public static OpenRunResult Success(Guid runId) => new(true, runId, null);

    public static OpenRunResult Rejected(string error) => new(false, null, error);
}

public sealed class AddPayrollItem(IPayrollRunStore store, IAuditTrail audit)
{
    public async Task<AddItemOutcome> ExecuteAsync(
        Guid runId,
        Guid employeeId,
        decimal grossSalary,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var folha = await store.FindForUpdateAsync(runId, cancellationToken);

        if (folha is null)
        {
            return AddItemOutcome.NotFound;
        }

        try
        {
            folha.AddItem(employeeId, grossSalary);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return AddItemOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                PayrollAuditActions.ItemAdded,
                PayrollAuditEntityTypes.Run,
                folha.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{employeeId}}","grossSalary":{{grossSalary}}}"""),
            cancellationToken);

        return AddItemOutcome.Added;
    }
}

/// <summary>
/// Submete a folha a `approval`. Mesmo desenho de
/// `SubmitRequisition` (`procurement`) — ver ali o comentário completo sobre
/// porque a inversão de dependência vive no composition root.
/// </summary>
public sealed class SubmitPayrollRun(
    IPayrollRunStore store,
    IPayrollApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<SubmitRunResult> ExecuteAsync(
        Guid runId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var folha = await store.FindForUpdateAsync(runId, cancellationToken);

        if (folha is null)
        {
            return SubmitRunResult.NotFound();
        }

        if (!approvals.IsAvailable)
        {
            return SubmitRunResult.ApprovalUnavailable();
        }

        if (folha.Status is not PayrollRunStatus.Draft)
        {
            return SubmitRunResult.Rejected(
                $"Só um rascunho se submete. Esta folha está em {folha.Status}.");
        }

        if (folha.Items.Count == 0)
        {
            return SubmitRunResult.Rejected("Uma folha sem itens não tem o que aprovar.");
        }

        var submissao = await approvals.SubmitAsync(
            folha.Id,
            folha.OpenedByEmployeeId,
            folha.TotalGross,
            $"Folha de pagamento {folha.Year}/{folha.Month:D2}",
            cancellationToken);

        if (!submissao.Submitted)
        {
            return SubmitRunResult.SubmissionFailed(submissao.Reason!);
        }

        folha.MarkSubmitted(submissao.RequestId!.Value, clock.GetUtcNow());

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                PayrollAuditActions.RunSubmitted,
                PayrollAuditEntityTypes.Run,
                folha.Id.ToString(),
                context,
                NewValue: $$"""{"approvalRequest":"{{folha.ApprovalRequestId}}","totalGross":{{folha.TotalGross}}}"""),
            cancellationToken);

        return SubmitRunResult.Success(folha.ApprovalRequestId!.Value);
    }
}

/// <summary>
/// Aplica a decisão de `approval`, se já houver uma. Mesmo desenho de
/// `ApplyRequisitionDecision` — `payroll` pergunta, `approval` nunca empurra.
/// </summary>
public sealed class ApplyPayrollDecision(
    IPayrollRunStore store,
    IPayrollApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<ApplyDecisionResult> ExecuteAsync(
        Guid runId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var folha = await store.FindForUpdateAsync(runId, cancellationToken);

        if (folha is null)
        {
            return ApplyDecisionResult.NotFound();
        }

        if (folha.Status is not PayrollRunStatus.PendingApproval)
        {
            return ApplyDecisionResult.Settled(folha.Status.ToString());
        }

        var estado = await approvals.GetStateAsync(folha.ApprovalRequestId!.Value, cancellationToken);

        if (estado is PayrollApprovalState.Pending or PayrollApprovalState.Unknown)
        {
            return ApplyDecisionResult.StillPending();
        }

        if (estado is PayrollApprovalState.Approved)
        {
            folha.MarkApproved(clock.GetUtcNow());
        }
        else
        {
            folha.MarkRefused(clock.GetUtcNow());
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                folha.Status is PayrollRunStatus.Approved
                    ? PayrollAuditActions.RunApproved
                    : PayrollAuditActions.RunRefused,
                PayrollAuditEntityTypes.Run,
                folha.Id.ToString(),
                context,
                NewValue: $$"""{"status":"{{folha.Status}}"}"""),
            cancellationToken);

        return ApplyDecisionResult.Applied(folha.Status.ToString());
    }
}

public enum AddItemOutcome
{
    Added,
    NotFound,
    Rejected,
}

public sealed record SubmitRunResult(SubmitRunOutcome Outcome, Guid? ApprovalRequestId, string? Error)
{
    public static SubmitRunResult Success(Guid approvalRequestId) =>
        new(SubmitRunOutcome.Submitted, approvalRequestId, null);

    public static SubmitRunResult NotFound() =>
        new(SubmitRunOutcome.NotFound, null, "Folha não encontrada.");

    public static SubmitRunResult ApprovalUnavailable() =>
        new(SubmitRunOutcome.ApprovalUnavailable, null,
            "Sem motor de governança ligado neste ambiente.");

    public static SubmitRunResult Rejected(string error) => new(SubmitRunOutcome.Rejected, null, error);

    public static SubmitRunResult SubmissionFailed(string error) =>
        new(SubmitRunOutcome.SubmissionFailed, null, error);
}

public enum SubmitRunOutcome
{
    Submitted,
    NotFound,
    ApprovalUnavailable,
    Rejected,
    SubmissionFailed,
}

public sealed record ApplyDecisionResult(ApplyDecisionOutcome Outcome, string? Status, string? Error)
{
    public static ApplyDecisionResult Applied(string status) =>
        new(ApplyDecisionOutcome.Applied, status, null);

    public static ApplyDecisionResult StillPending() =>
        new(ApplyDecisionOutcome.StillPending, PayrollRunStatus.PendingApproval.ToString(), null);

    public static ApplyDecisionResult Settled(string status) =>
        new(ApplyDecisionOutcome.AlreadySettled, status, null);

    public static ApplyDecisionResult NotFound() =>
        new(ApplyDecisionOutcome.NotFound, null, "Folha não encontrada.");
}

public enum ApplyDecisionOutcome
{
    Applied,
    StillPending,
    AlreadySettled,
    NotFound,
}

public static class PayrollAuditActions
{
    public const string RunOpened = "payroll.run.opened";
    public const string ItemAdded = "payroll.run.item_added";
    public const string RunSubmitted = "payroll.run.submitted";
    public const string RunApproved = "payroll.run.approved";
    public const string RunRefused = "payroll.run.refused";
}

public static class PayrollAuditEntityTypes
{
    public const string Run = "payroll.run";
}
