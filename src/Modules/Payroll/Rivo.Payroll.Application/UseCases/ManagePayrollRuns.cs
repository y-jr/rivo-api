using Rivo.Audit.Contracts;
using Rivo.Fiscal.Contracts;
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

/// <summary>
/// Acrescenta um item à folha, já com o cálculo fiscal aplicado.
///
/// <para>
/// <strong>`payroll` pergunta, `fiscal` responde.</strong> A ordem é a do
/// artigo 7.º do Código do IRT: primeiro desconta-se o INSS a cargo do
/// trabalhador, depois deduzem-se os subsídios isentos (Alimentação e
/// Transporte, cada um até o limiar em vigor — Férias e Natal não têm
/// isenção, ver `PayrollItem`), e só depois se calcula o IRT sobre a
/// matéria colectável resultante — nunca sobre o bruto. Todas as perguntas
/// são feitas à data do facto gerador (o fim do período da folha,
/// <see cref="PayrollRun.PeriodEndDate"/>), nunca à data corrente
/// (ADR-011 §3).
/// </para>
///
/// <para>
/// <strong>Recusa, não omissão.</strong> Se `fiscal` não tiver taxa de INSS,
/// tabela de IRT, ou limiar de isenção (só quando o subsídio correspondente
/// é declarado) em vigor para a data, o item não nasce — mesmo padrão de
/// `IssueSalesInvoice` perante `TaxDeterminationOutcome.NoRateInForce`:
/// inventar o valor seria pior do que recusar.
/// </para>
/// </summary>
public sealed class AddPayrollItem(
    IPayrollRunStore store,
    ITaxDetermination taxes,
    IIncomeTaxDetermination incomeTax,
    ISubsidyExemptionDetermination subsidyExemptions,
    IAuditTrail audit)
{
    public async Task<AddItemOutcome> ExecuteAsync(
        Guid runId,
        Guid employeeId,
        decimal grossSalary,
        decimal foodAllowance,
        decimal transportAllowance,
        decimal vacationAllowance,
        decimal christmasAllowance,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var folha = await store.FindForUpdateAsync(runId, cancellationToken);

        if (folha is null)
        {
            return AddItemOutcome.NotFound();
        }

        var facto = folha.PeriodEndDate;

        var inss = await taxes.DetermineAsync(
            new TaxDeterminationRequest(TaxKind.EmployeeSocialSecurity, TaxCodes.SocialSecurity, facto),
            cancellationToken);

        if (inss.Outcome is not TaxDeterminationOutcome.Determined)
        {
            return AddItemOutcome.FiscalDataMissing(
                $"Não há taxa de INSS (trabalhador) em vigor a {facto:yyyy-MM-dd}. " +
                "Configure a taxa em /fiscal/tax-rates antes de calcular a folha.");
        }

        var inssTrabalhador = grossSalary * (inss.Determination!.Percentage / 100m);

        // Só se pergunta o limiar quando há subsídio declarado — um item sem
        // alimentação nem transporte não depende de nenhum dos dois estar
        // configurado.
        var isencaoAlimentacao = 0m;

        if (foodAllowance > 0)
        {
            var limiar = await subsidyExemptions.DetermineAsync(
                new SubsidyExemptionRequest(SubsidyKind.FoodAllowance, facto), cancellationToken);

            if (limiar.Outcome is not SubsidyExemptionOutcome.Determined)
            {
                return AddItemOutcome.FiscalDataMissing(
                    $"Não há limiar de isenção de subsídio de alimentação em vigor a {facto:yyyy-MM-dd}. " +
                    "Configure o limiar em /fiscal/subsidy-exemptions antes de calcular a folha.");
            }

            isencaoAlimentacao = Math.Min(foodAllowance, limiar.Exemption!.Amount);
        }

        var isencaoTransporte = 0m;

        if (transportAllowance > 0)
        {
            var limiar = await subsidyExemptions.DetermineAsync(
                new SubsidyExemptionRequest(SubsidyKind.TransportAllowance, facto), cancellationToken);

            if (limiar.Outcome is not SubsidyExemptionOutcome.Determined)
            {
                return AddItemOutcome.FiscalDataMissing(
                    $"Não há limiar de isenção de subsídio de transporte em vigor a {facto:yyyy-MM-dd}. " +
                    "Configure o limiar em /fiscal/subsidy-exemptions antes de calcular a folha.");
            }

            isencaoTransporte = Math.Min(transportAllowance, limiar.Exemption!.Amount);
        }

        // Férias e Natal não entram aqui — tributados normalmente, já fazem
        // parte do bruto sem dedução nenhuma (confirmado pelo utilizador).
        var materiaColectavel = grossSalary - inssTrabalhador - isencaoAlimentacao - isencaoTransporte;

        var irt = await incomeTax.DetermineAsync(
            new IncomeTaxDeterminationRequest(materiaColectavel, facto),
            cancellationToken);

        if (irt.Outcome is not IncomeTaxDeterminationOutcome.Determined)
        {
            return AddItemOutcome.FiscalDataMissing(
                $"Não há tabela de escalões de IRT em vigor a {facto:yyyy-MM-dd}. " +
                "Configure a tabela em /fiscal/income-tax-schedule antes de calcular a folha.");
        }

        PayrollItem item;

        try
        {
            item = folha.AddItem(
                employeeId, grossSalary, foodAllowance, transportAllowance, vacationAllowance, christmasAllowance);
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or ArgumentException)
        {
            // Campo mal preenchido (bruto não positivo, subsídio negativo, ou
            // subsídios que não cabem no bruto) — 400.
            return AddItemOutcome.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Conflito com o estado corrente da folha (já não está em
            // rascunho) — 409, não 400: o pedido está bem formado, é o
            // recurso que já não aceita a operação.
            return AddItemOutcome.Conflict(error.Message);
        }

        item.ApplyCalculation(irt.Determination!.Amount, inssTrabalhador);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                PayrollAuditActions.ItemAdded,
                PayrollAuditEntityTypes.Run,
                folha.Id.ToString(),
                context,
                NewValue: $$"""
                    {"employeeId":"{{employeeId}}","grossSalary":{{grossSalary}},"foodAllowance":{{foodAllowance}},"transportAllowance":{{transportAllowance}},"vacationAllowance":{{vacationAllowance}},"christmasAllowance":{{christmasAllowance}},"withholdingTax":{{item.WithholdingTax}},"socialSecurityContribution":{{item.SocialSecurityContribution}},"netSalary":{{item.NetSalary}}}
                    """),
            cancellationToken);

        return AddItemOutcome.Added(item.Id);
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

public sealed record AddItemOutcome(AddItemResultKind Outcome, Guid? ItemId, string? Error)
{
    public static AddItemOutcome Added(Guid itemId) => new(AddItemResultKind.Added, itemId, null);

    public static AddItemOutcome NotFound() =>
        new(AddItemResultKind.NotFound, null, "Folha não encontrada.");

    /// <summary>Campo mal preenchido no item — 400.</summary>
    public static AddItemOutcome Rejected(string error) => new(AddItemResultKind.Rejected, null, error);

    /// <summary>Conflito com o estado corrente da folha — 409.</summary>
    public static AddItemOutcome Conflict(string error) => new(AddItemResultKind.Conflict, null, error);

    /// <summary>
    /// `fiscal` não tem taxa/tabela em vigor à data do facto gerador — 400:
    /// o pedido está bem formado, falta configuração fiscal para o satisfazer
    /// (mesmo padrão de `IssueSalesInvoice` perante `NoRateInForce`).
    /// </summary>
    public static AddItemOutcome FiscalDataMissing(string error) =>
        new(AddItemResultKind.FiscalDataMissing, null, error);
}

public enum AddItemResultKind
{
    Added,
    NotFound,
    Rejected,
    Conflict,
    FiscalDataMissing,
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
    public const string DocumentAttached = "payroll.item.document_attached";
}

public static class PayrollAuditEntityTypes
{
    public const string Run = "payroll.run";
    public const string Item = "payroll.item";
}
