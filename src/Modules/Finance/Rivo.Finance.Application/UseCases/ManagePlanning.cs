using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

// ---------- Centros de custo ----------

public sealed class OpenCostCentre(IPlanningStore store, IAuditTrail audit)
{
    public async Task<OpenCostCentreResult> ExecuteAsync(
        string code,
        string name,
        Guid? departmentId,
        Guid responsibleEmployeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var normalizado = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizado.Length > 0 &&
            await store.CostCentreCodeExistsAsync(normalizado, cancellationToken))
        {
            return OpenCostCentreResult.Duplicate(
                $"Já existe um centro de custo com o código '{normalizado}'.");
        }

        CostCentre centro;

        try
        {
            centro = CostCentre.Open(normalizado, name, departmentId, responsibleEmployeeId);
        }
        catch (ArgumentException error)
        {
            return OpenCostCentreResult.Rejected(error.Message);
        }

        await store.AddCostCentreAsync(centro, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.CostCentreOpened,
                FinanceAuditEntityTypes.CostCentre,
                centro.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{centro.Code}}","name":"{{centro.Name}}","department":"{{centro.DepartmentId}}","responsible":"{{centro.ResponsibleEmployeeId}}"}"""),
            cancellationToken);

        return OpenCostCentreResult.Success(centro.Id);
    }
}

public sealed record OpenCostCentreResult(
    OpenCostCentreOutcome Outcome,
    Guid? CostCentreId,
    string? Error)
{
    public static OpenCostCentreResult Success(Guid id) =>
        new(OpenCostCentreOutcome.Opened, id, null);

    public static OpenCostCentreResult Duplicate(string error) =>
        new(OpenCostCentreOutcome.Duplicate, null, error);

    public static OpenCostCentreResult Rejected(string error) =>
        new(OpenCostCentreOutcome.Rejected, null, error);
}

public enum OpenCostCentreOutcome
{
    Opened,
    Duplicate,
    Rejected,
}

public sealed class ListCostCentres(IPlanningStore store)
{
    public async Task<IReadOnlyList<CostCentreView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var centros = await store.ListCostCentresAsync(includeInactive, cancellationToken);

        return [.. centros.Select(c => new CostCentreView(
            c.Id, c.Code, c.Name, c.DepartmentId, c.ResponsibleEmployeeId, c.IsActive))];
    }
}

/// <param name="DepartmentId">
/// Nulo é um estado normal, não dado em falta: o mapeamento a Departamento é
/// opcional por desenho (D4).
/// </param>
public sealed record CostCentreView(
    Guid CostCentreId,
    string Code,
    string Name,
    Guid? DepartmentId,
    Guid ResponsibleEmployeeId,
    bool IsActive);

// ---------- Orçamentos ----------

public sealed class DraftBudget(IPlanningStore store, IAuditTrail audit)
{
    public async Task<DraftBudgetResult> ExecuteAsync(
        Guid costCentreId,
        int fiscalYear,
        string currency,
        IReadOnlyDictionary<int, decimal> monthlyCeilings,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var centro = await store.FindCostCentreAsync(costCentreId, cancellationToken);

        if (centro is null)
        {
            return DraftBudgetResult.CostCentreNotFound();
        }

        // Dois orçamentos para o mesmo centro e ano tornariam a verificação de
        // BR-8 ambígua — e uma verificação ambígua não verifica nada.
        if (await store.FindBudgetForAsync(costCentreId, fiscalYear, cancellationToken) is not null)
        {
            return DraftBudgetResult.Duplicate(
                $"O centro de custo {centro.Code} já tem orçamento para {fiscalYear}.");
        }

        Budget orcamento;

        try
        {
            orcamento = Budget.Draft(costCentreId, fiscalYear, currency);

            foreach (var (mes, tecto) in monthlyCeilings ?? new Dictionary<int, decimal>())
            {
                orcamento.SetMonth(mes, tecto);
            }
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return DraftBudgetResult.Rejected(error.Message);
        }

        await store.AddBudgetAsync(orcamento, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.BudgetDrafted,
                FinanceAuditEntityTypes.Budget,
                orcamento.Id.ToString(),
                context,
                NewValue: $$"""{"costCentre":"{{centro.Code}}","fiscalYear":{{fiscalYear}},"annualTotal":{{orcamento.AnnualTotal}},"currency":"{{orcamento.Currency}}"}"""),
            cancellationToken);

        return DraftBudgetResult.Success(orcamento.Id);
    }
}

public sealed record DraftBudgetResult(DraftBudgetOutcome Outcome, Guid? BudgetId, string? Error)
{
    public static DraftBudgetResult Success(Guid id) => new(DraftBudgetOutcome.Drafted, id, null);

    public static DraftBudgetResult CostCentreNotFound() =>
        new(DraftBudgetOutcome.CostCentreNotFound, null, null);

    public static DraftBudgetResult Duplicate(string error) =>
        new(DraftBudgetOutcome.Duplicate, null, error);

    public static DraftBudgetResult Rejected(string error) =>
        new(DraftBudgetOutcome.Rejected, null, error);
}

public enum DraftBudgetOutcome
{
    Drafted,
    CostCentreNotFound,
    Duplicate,
    Rejected,
}

public sealed class ReviseBudget(IPlanningStore store, IAuditTrail audit)
{
    public async Task<ReviseBudgetResult> ExecuteAsync(
        Guid budgetId,
        IReadOnlyDictionary<int, decimal> monthlyCeilings,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var orcamento = await store.FindBudgetForUpdateAsync(budgetId, cancellationToken);

        if (orcamento is null)
        {
            return ReviseBudgetResult.NotFound();
        }

        try
        {
            foreach (var (mes, tecto) in monthlyCeilings ?? new Dictionary<int, decimal>())
            {
                orcamento.SetMonth(mes, tecto);
            }
        }
        catch (InvalidOperationException error)
        {
            // Aprovado não se altera: subir o tecto depois de aprovado
            // esvaziaria a aprovação, e com ela BR-8.
            return ReviseBudgetResult.NotDraft(error.Message);
        }
        catch (ArgumentOutOfRangeException error)
        {
            return ReviseBudgetResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.BudgetRevised,
                FinanceAuditEntityTypes.Budget,
                orcamento.Id.ToString(),
                context,
                NewValue: $$"""{"annualTotal":{{orcamento.AnnualTotal}}}"""),
            cancellationToken);

        return ReviseBudgetResult.Success();
    }
}

public sealed record ReviseBudgetResult(ReviseBudgetOutcome Outcome, string? Error)
{
    public static ReviseBudgetResult Success() => new(ReviseBudgetOutcome.Revised, null);

    public static ReviseBudgetResult NotFound() => new(ReviseBudgetOutcome.NotFound, null);

    public static ReviseBudgetResult NotDraft(string error) =>
        new(ReviseBudgetOutcome.NotDraft, error);

    public static ReviseBudgetResult Rejected(string error) =>
        new(ReviseBudgetOutcome.Rejected, error);
}

public enum ReviseBudgetOutcome
{
    Revised,
    NotFound,

    /// <summary>Já aprovado. Conflito de estado — 409.</summary>
    NotDraft,

    Rejected,
}

/// <summary>
/// Põe um orçamento em vigor.
///
/// <para>
/// <strong>É este acto que dá a BR-8 alguma coisa contra que verificar.</strong>
/// Um rascunho não controla nada — verificar contra números que ninguém aprovou
/// seria dar uma resposta sem valor.
/// </para>
/// </summary>
public sealed class ApproveBudget(IPlanningStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<ApproveBudgetResult> ExecuteAsync(
        Guid budgetId,
        Guid approvedByEmployeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var orcamento = await store.FindBudgetForUpdateAsync(budgetId, cancellationToken);

        if (orcamento is null)
        {
            return ApproveBudgetResult.NotFound();
        }

        try
        {
            orcamento.Approve(approvedByEmployeeId, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return ApproveBudgetResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.BudgetApproved,
                FinanceAuditEntityTypes.Budget,
                orcamento.Id.ToString(),
                context,
                NewValue: $$"""{"fiscalYear":{{orcamento.FiscalYear}},"annualTotal":{{orcamento.AnnualTotal}},"approvedBy":"{{approvedByEmployeeId}}"}"""),
            cancellationToken);

        return ApproveBudgetResult.Success();
    }
}

public sealed record ApproveBudgetResult(ApproveBudgetOutcome Outcome, string? Error)
{
    public static ApproveBudgetResult Success() => new(ApproveBudgetOutcome.Approved, null);

    public static ApproveBudgetResult NotFound() => new(ApproveBudgetOutcome.NotFound, null);

    public static ApproveBudgetResult Rejected(string error) =>
        new(ApproveBudgetOutcome.Rejected, error);
}

public enum ApproveBudgetOutcome
{
    Approved,
    NotFound,
    Rejected,
}

public sealed class ListBudgets(IPlanningStore store)
{
    public async Task<IReadOnlyList<BudgetView>> ExecuteAsync(
        Guid? costCentreId,
        int? fiscalYear,
        CancellationToken cancellationToken)
    {
        var orcamentos = await store.ListBudgetsAsync(costCentreId, fiscalYear, cancellationToken);

        return [.. orcamentos.Select(b => new BudgetView(
            b.Id, b.CostCentreId, b.FiscalYear, b.Currency, b.Status.ToString(),
            b.AnnualTotal, b.ApprovedAt, b.ApprovedByEmployeeId,
            [.. b.Lines.OrderBy(l => l.Month).Select(l => new BudgetLineView(l.Month, l.Amount))]))];
    }
}

public sealed record BudgetView(
    Guid BudgetId,
    Guid CostCentreId,
    int FiscalYear,
    string Currency,
    string Status,
    decimal AnnualTotal,
    DateTimeOffset? ApprovedAt,
    Guid? ApprovedByEmployeeId,
    IReadOnlyList<BudgetLineView> Lines);

public sealed record BudgetLineView(int Month, decimal Amount);

// ---------- Previsão de custos departamentais ----------

/// <summary>
/// Regista a previsão de custos de um departamento para um mês.
///
/// <para>
/// <strong>Não é um orçamento</strong> (D3). O orçamento é do centro de custo e
/// é um tecto de controlo; isto é do departamento e é input ao carregamento de
/// caixa. Coexistem sobre o mesmo período sem se fundirem — e nada aqui olha
/// para o orçamento, de propósito.
/// </para>
/// </summary>
public sealed class RecordCostForecast(IPlanningStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RecordForecastResult> ExecuteAsync(
        Guid departmentId,
        int fiscalYear,
        int month,
        string currency,
        decimal operationalCosts,
        decimal fixedCosts,
        bool submit,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (await store.ForecastExistsAsync(departmentId, fiscalYear, month, cancellationToken))
        {
            return RecordForecastResult.Duplicate(
                $"Já existe previsão para este departamento em {fiscalYear}/{month:00}.");
        }

        DepartmentCostForecast previsao;

        try
        {
            previsao = DepartmentCostForecast.Draft(
                departmentId, fiscalYear, month, currency, operationalCosts, fixedCosts);

            if (submit)
            {
                previsao.Submit(clock.GetUtcNow());
            }
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return RecordForecastResult.Rejected(error.Message);
        }

        await store.AddForecastAsync(previsao, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        if (submit)
        {
            await audit.RecordAsync(
                new AuditRecord(
                    FinanceAuditActions.ForecastSubmitted,
                    FinanceAuditEntityTypes.CostForecast,
                    previsao.Id.ToString(),
                    context,
                    NewValue: $$"""{"department":"{{departmentId}}","period":"{{fiscalYear}}-{{month:00}}","total":{{previsao.Total}}}"""),
                cancellationToken);
        }

        return RecordForecastResult.Success(previsao.Id);
    }
}

public sealed record RecordForecastResult(
    RecordForecastOutcome Outcome,
    Guid? ForecastId,
    string? Error)
{
    public static RecordForecastResult Success(Guid id) =>
        new(RecordForecastOutcome.Recorded, id, null);

    public static RecordForecastResult Duplicate(string error) =>
        new(RecordForecastOutcome.Duplicate, null, error);

    public static RecordForecastResult Rejected(string error) =>
        new(RecordForecastOutcome.Rejected, null, error);
}

public enum RecordForecastOutcome
{
    Recorded,
    Duplicate,
    Rejected,
}

public sealed class ListCostForecasts(IPlanningStore store)
{
    public async Task<IReadOnlyList<CostForecastView>> ExecuteAsync(
        Guid? departmentId,
        int? fiscalYear,
        CancellationToken cancellationToken)
    {
        var previsoes = await store.ListForecastsAsync(departmentId, fiscalYear, cancellationToken);

        return [.. previsoes.Select(f => new CostForecastView(
            f.Id, f.DepartmentId, f.FiscalYear, f.Month, f.Currency,
            f.OperationalCosts, f.FixedCosts, f.Total, f.Status.ToString(), f.SubmittedAt))];
    }
}

public sealed record CostForecastView(
    Guid ForecastId,
    Guid DepartmentId,
    int FiscalYear,
    int Month,
    string Currency,
    decimal OperationalCosts,
    decimal FixedCosts,
    decimal Total,
    string Status,
    DateTimeOffset? SubmittedAt);

// ---------- BR-8 ----------

/// <summary>
/// O disponível orçamental — <strong>a implementação de BR-8</strong>.
///
/// <para>
/// `approval` pergunta se um valor cabe. Nada mais atravessa a fronteira: nem
/// orçamentos, nem centros de custo, nem lançamentos. É um dos dois pontos onde
/// o `docs` avisa que o God Module pode nascer, e a estreiteza do contrato é a
/// mitigação.
/// </para>
///
/// <para>
/// <strong>Quatro dos cinco resultados são recusa.</strong> Uma política que
/// exige verificação orçamental está a dizer que não se decide sem saber — e
/// "não consegui verificar" não é "pode avançar". Aprovar por omissão seria
/// exactamente o modo de falha que BR-8 existe para impedir.
/// </para>
/// </summary>
public sealed class BudgetAvailability(IPlanningStore store) : IBudgetAvailability
{
    public async Task<BudgetCheckResult> CheckAsync(
        BudgetCheck check,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);

        var (centro, recusa) = await ResolveCostCentreAsync(check, cancellationToken);

        if (centro is null)
        {
            return recusa!;
        }

        var orcamento = await store.FindBudgetForAsync(centro.Id, check.On.Year, cancellationToken);

        if (orcamento is null || !orcamento.IsInForce)
        {
            return BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.NoBudget,
                $"O centro de custo {centro.Code} não tem orçamento aprovado para {check.On.Year}. " +
                "Um rascunho não controla nada.");
        }

        // Sem conversão automática, pela mesma razão que a execução de
        // pagamento a recusa: o câmbio é uma decisão, e ninguém a tomou aqui.
        if (!string.Equals(orcamento.Currency, check.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.CurrencyMismatch,
                $"O pedido é em {check.Currency} e o orçamento de {centro.Code} é em " +
                $"{orcamento.Currency}. Não há conversão automática.");
        }

        if (orcamento.CeilingFor(check.On.Month) is not { } tecto)
        {
            return BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.NoBudget,
                $"O orçamento de {centro.Code} não tem tecto para {check.On.Year}/{check.On.Month:00}.");
        }

        var comprometido = await store.CommittedAgainstAsync(
            centro.Id, check.On.Year, check.On.Month, cancellationToken);

        var disponivel = tecto - comprometido;

        return check.Amount <= disponivel
            ? BudgetCheckResult.Within(tecto, comprometido, disponivel)
            : BudgetCheckResult.Exceeded(
                tecto, comprometido, disponivel,
                $"O tecto de {centro.Code} para {check.On.Year}/{check.On.Month:00} é " +
                $"{tecto:N2} {orcamento.Currency}, já tem {comprometido:N2} comprometidos, " +
                $"e este pedido é de {check.Amount:N2}.");
    }

    /// <summary>
    /// A rubrica primeiro, o departamento como recuo.
    ///
    /// <para>
    /// Quem submeteu sabendo o centro de custo manda-o em
    /// <see cref="BudgetCheck.Reference"/>, e não há ambiguidade nenhuma a
    /// resolver. Quem não sabe manda o departamento — e aí a ambiguidade é
    /// real, porque o mapeamento não é 1:1 (D4).
    /// </para>
    /// </summary>
    private async Task<(CostCentre? Centre, BudgetCheckResult? Refusal)> ResolveCostCentreAsync(
        BudgetCheck check,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(check.Reference))
        {
            if (!Guid.TryParse(check.Reference, out var identificador))
            {
                return (null, BudgetCheckResult.Unverifiable(
                    BudgetCheckOutcome.NoCostCentre,
                    $"A rubrica '{check.Reference}' não é um centro de custo reconhecível."));
            }

            var indicado = await store.FindCostCentreAsync(identificador, cancellationToken);

            if (indicado is null || !indicado.IsActive)
            {
                return (null, BudgetCheckResult.Unverifiable(
                    BudgetCheckOutcome.NoCostCentre,
                    "O centro de custo indicado não existe ou está desactivado."));
            }

            return (indicado, null);
        }

        if (check.DepartmentId is not { } departamento)
        {
            return (null, BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.NoCostCentre,
                "O processo não indica rubrica nem departamento, e sem um dos dois não há " +
                "orçamento contra que verificar."));
        }

        var centros = await store.ListCostCentresForDepartmentAsync(departamento, cancellationToken);
        var activos = centros.Where(c => c.IsActive).ToList();

        if (activos.Count == 0)
        {
            return (null, BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.NoCostCentre,
                "Nenhum centro de custo activo está associado a este departamento. " +
                "O mapeamento é opcional por desenho (D4) — mas sem ele BR-8 não pode verificar."));
        }

        // Vários centros de custo no mesmo departamento é estado legítimo (D4
        // diz que não é 1:1). O que não é legítimo é escolher um: seria
        // verificar contra um tecto que ninguém indicou.
        if (activos.Count > 1)
        {
            return (null, BudgetCheckResult.Unverifiable(
                BudgetCheckOutcome.NoCostCentre,
                $"{activos.Count} centros de custo estão associados a este departamento. " +
                "Não há tecto único a verificar — o pedido tem de indicar a rubrica."));
        }

        return (activos[0], null);
    }
}
