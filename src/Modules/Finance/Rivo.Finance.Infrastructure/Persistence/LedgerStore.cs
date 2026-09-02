using Microsoft.EntityFrameworkCore;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class LedgerStore(FinanceDbContext context) : ILedgerStore
{
    public async Task<LedgerAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await context.LedgerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public async Task<LedgerAccount?> FindAccountByCodeAsync(string code, CancellationToken cancellationToken) =>
        await context.LedgerAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == code, cancellationToken);

    public async Task<LedgerAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken) =>
        await context.LedgerAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public async Task<IReadOnlyDictionary<string, LedgerAccount>> FindAccountsByCodeAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken)
    {
        if (codes.Count == 0)
        {
            return new Dictionary<string, LedgerAccount>(StringComparer.Ordinal);
        }

        var contas = await context.LedgerAccounts
            .AsNoTracking()
            .Where(a => codes.Contains(a.Code))
            .ToListAsync(cancellationToken);

        return contas.ToDictionary(a => a.Code, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<LedgerAccount>> ListAccountsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.LedgerAccounts.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query.OrderBy(a => a.Code).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasChildrenAsync(Guid accountId, CancellationToken cancellationToken) =>
        await context.LedgerAccounts
            .AsNoTracking()
            .AnyAsync(a => a.ParentId == accountId && a.IsActive, cancellationToken);

    public async Task<bool> HasPostingsAsync(Guid accountId, CancellationToken cancellationToken) =>
        await context.JournalEntries
            .AsNoTracking()
            .SelectMany(e => e.Lines)
            .AnyAsync(l => l.AccountId == accountId, cancellationToken);

    public async Task AddAccountAsync(LedgerAccount account, CancellationToken cancellationToken) =>
        await context.LedgerAccounts.AddAsync(account, cancellationToken);

    public async Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken) =>
        await context.Journals
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == journalId, cancellationToken);

    public async Task<Journal?> FindJournalByCodeAsync(string code, CancellationToken cancellationToken) =>
        await context.Journals
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Journal>> ListJournalsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.Journals.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(j => j.IsActive);
        }

        return await query.OrderBy(j => j.Code).ToListAsync(cancellationToken);
    }

    public async Task AddJournalAsync(Journal journal, CancellationToken cancellationToken) =>
        await context.Journals.AddAsync(journal, cancellationToken);

    public async Task<JournalEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
        await context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

    public async Task<JournalEntry?> FindEntryForUpdateAsync(Guid entryId, CancellationToken cancellationToken) =>
        await context.JournalEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken);

    public async Task<JournalEntry?> FindEntryByArchivalNumberAsync(
        string archivalNumber, CancellationToken cancellationToken) =>
        await context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ArchivalNumber == archivalNumber, cancellationToken);

    public async Task<IReadOnlyList<JournalEntry>> ListEntriesAsync(
        Guid? journalId,
        int? fiscalYear,
        int? period,
        CancellationToken cancellationToken)
    {
        var query = context.JournalEntries.AsNoTracking().AsQueryable();

        if (journalId is { } diario)
        {
            query = query.Where(e => e.JournalId == diario);
        }

        if (fiscalYear is { } ano)
        {
            query = query.Where(e => e.TransactionDate.Year == ano);
        }

        if (period is { } numero)
        {
            query = query.Where(e => e.Period == numero);
        }

        return await query
            .OrderBy(e => e.TransactionDate)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> EntryExistsAsync(
        DateOnly transactionDate,
        string journalCode,
        string archivalNumber,
        CancellationToken cancellationToken) =>
        await context.JournalEntries
            .AsNoTracking()
            .AnyAsync(
                e => e.TransactionDate == transactionDate
                    && e.JournalCode == journalCode
                    && e.ArchivalNumber == archivalNumber,
                cancellationToken);

    public async Task AddEntryAsync(JournalEntry entry, CancellationToken cancellationToken) =>
        await context.JournalEntries.AddAsync(entry, cancellationToken);

    public async Task<AccountingPeriod?> FindPeriodAsync(
        int fiscalYear,
        int number,
        CancellationToken cancellationToken) =>
        await context.AccountingPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FiscalYear == fiscalYear && p.Number == number, cancellationToken);

    public async Task<AccountingPeriod?> FindPeriodForUpdateAsync(
        int fiscalYear,
        int number,
        CancellationToken cancellationToken) =>
        // Rastreado: quem o procura assim vai fechá-lo ou reabri-lo, e o
        // contador de concorrência desta linha é o que impede um lançamento de
        // cair dentro de um período a ser fechado (BR-17).
        await context.AccountingPeriods
            .FirstOrDefaultAsync(p => p.FiscalYear == fiscalYear && p.Number == number, cancellationToken);

    public async Task<IReadOnlyList<AccountingPeriod>> ListPeriodsAsync(
        int fiscalYear,
        CancellationToken cancellationToken) =>
        await context.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.FiscalYear == fiscalYear)
            .OrderBy(p => p.Number)
            .ToListAsync(cancellationToken);

    public async Task AddPeriodAsync(AccountingPeriod period, CancellationToken cancellationToken) =>
        await context.AccountingPeriods.AddAsync(period, cancellationToken);

    public async Task<PostingRule?> FindActivePostingRuleAsync(
        PostingEvent postingEvent,
        CancellationToken cancellationToken) =>
        await context.PostingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Event == postingEvent && r.IsActive, cancellationToken);

    public async Task<PostingRule?> FindPostingRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
        await context.PostingRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

    public async Task<PostingRule?> FindPostingRuleForUpdateAsync(
        Guid ruleId,
        CancellationToken cancellationToken) =>
        await context.PostingRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

    public async Task<IReadOnlyList<PostingRule>> ListPostingRulesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.PostingRules.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query.OrderBy(r => r.Event).ToListAsync(cancellationToken);
    }

    public async Task AddPostingRuleAsync(PostingRule rule, CancellationToken cancellationToken) =>
        await context.PostingRules.AddAsync(rule, cancellationToken);

    public async Task<ChartOfAccountsVersion?> FindChartVersionAsync(
        Guid chartId,
        CancellationToken cancellationToken) =>
        await context.ChartOfAccountsVersions
            .AsNoTracking()
            .Include(v => v.Accounts)
            .FirstOrDefaultAsync(v => v.Id == chartId, cancellationToken);

    public async Task<ChartOfAccountsVersion?> FindChartVersionByKeyAsync(
        string jurisdiction,
        string name,
        string version,
        CancellationToken cancellationToken) =>
        await context.ChartOfAccountsVersions
            .AsNoTracking()
            .Include(v => v.Accounts)
            .FirstOrDefaultAsync(v =>
                v.Jurisdiction == jurisdiction &&
                v.Name == name &&
                v.Revision == version,
                cancellationToken);

    public async Task<IReadOnlyList<ChartOfAccountsVersion>> ListChartVersionsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.ChartOfAccountsVersions
            .AsNoTracking()
            .Include(v => v.Accounts)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(v => v.IsActive);
        }

        return await query
            .OrderBy(v => v.Jurisdiction)
            .ThenBy(v => v.Name)
            .ThenBy(v => v.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task AddChartVersionAsync(
        ChartOfAccountsVersion chartVersion,
        CancellationToken cancellationToken) =>
        await context.ChartOfAccountsVersions.AddAsync(chartVersion, cancellationToken);

    public async Task<ChartOfAccountsVersion?> FindActiveChartVersionForDateAsync(
        DateOnly date,
        CancellationToken cancellationToken) =>
        await context.ChartOfAccountsVersions
            .AsNoTracking()
            .Include(v => v.Accounts)
            .Where(v => v.IsActive &&
                        v.EffectiveFrom <= date &&
                        (v.EffectiveTo == null || v.EffectiveTo >= date))
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AccountingRule?> FindAccountingRuleAsync(
        Guid ruleId,
        CancellationToken cancellationToken) =>
        await context.AccountingRules
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);

    public async Task<IReadOnlyList<AccountingRule>> ListAccountingRulesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.AccountingRules
            .AsNoTracking()
            .Include(r => r.Lines)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .OrderBy(r => r.EffectiveFrom)
            .ThenBy(r => r.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAccountingRuleAsync(
        AccountingRule rule,
        CancellationToken cancellationToken) =>
        await context.AccountingRules.AddAsync(rule, cancellationToken);

    public async Task<IReadOnlyList<AccountMovement>> AccountMovementsAsync(
        int fiscalYear,
        int? uptoPeriod,
        CancellationToken cancellationToken)
    {
        // **Lançamentos anulados não contam.** Um balancete que os somasse
        // mostraria dinheiro que a anulação retirou.
        var query = context.JournalEntries
            .AsNoTracking()
            .Where(e => !e.IsVoided && e.TransactionDate.Year == fiscalYear);

        if (uptoPeriod is { } ate)
        {
            query = query.Where(e => e.Period <= ate);
        }

        var movimentos = await query
            .SelectMany(e => e.Lines)
            .GroupBy(l => new { l.AccountId, l.AccountCode })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.AccountCode,
                Debit = g.Where(l => l.Side == EntrySide.Debit).Sum(l => (decimal?)l.Amount) ?? 0m,
                Credit = g.Where(l => l.Side == EntrySide.Credit).Sum(l => (decimal?)l.Amount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return [.. movimentos.Select(m => new AccountMovement(
            m.AccountId, m.AccountCode, m.Debit, m.Credit))];
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}

public sealed class PlanningStore(FinanceDbContext context) : IPlanningStore
{
    public async Task<CostCentre?> FindCostCentreAsync(Guid costCentreId, CancellationToken cancellationToken) =>
        await context.CostCentres
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == costCentreId, cancellationToken);

    public async Task<CostCentre?> FindCostCentreForUpdateAsync(
        Guid costCentreId,
        CancellationToken cancellationToken) =>
        await context.CostCentres
            .FirstOrDefaultAsync(c => c.Id == costCentreId, cancellationToken);

    public async Task<bool> CostCentreCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        await context.CostCentres.AsNoTracking().AnyAsync(c => c.Code == code, cancellationToken);

    public async Task<IReadOnlyList<CostCentre>> ListCostCentresForDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken) =>
        await context.CostCentres
            .AsNoTracking()
            .Where(c => c.DepartmentId == departmentId)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CostCentre>> ListCostCentresAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.CostCentres.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.Code).ToListAsync(cancellationToken);
    }

    public async Task AddCostCentreAsync(CostCentre costCentre, CancellationToken cancellationToken) =>
        await context.CostCentres.AddAsync(costCentre, cancellationToken);

    public async Task<Budget?> FindBudgetAsync(Guid budgetId, CancellationToken cancellationToken) =>
        await context.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == budgetId, cancellationToken);

    public async Task<Budget?> FindBudgetForUpdateAsync(Guid budgetId, CancellationToken cancellationToken) =>
        await context.Budgets
            .FirstOrDefaultAsync(b => b.Id == budgetId, cancellationToken);

    public async Task<Budget?> FindBudgetForAsync(
        Guid costCentreId,
        int fiscalYear,
        CancellationToken cancellationToken) =>
        await context.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.CostCentreId == costCentreId && b.FiscalYear == fiscalYear,
                cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListBudgetsAsync(
        Guid? costCentreId,
        int? fiscalYear,
        CancellationToken cancellationToken)
    {
        var query = context.Budgets.AsNoTracking().AsQueryable();

        if (costCentreId is { } centro)
        {
            query = query.Where(b => b.CostCentreId == centro);
        }

        if (fiscalYear is { } ano)
        {
            query = query.Where(b => b.FiscalYear == ano);
        }

        return await query
            .OrderBy(b => b.FiscalYear)
            .ThenBy(b => b.CostCentreId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken) =>
        await context.Budgets.AddAsync(budget, cancellationToken);

    public async Task<decimal> CommittedAgainstAsync(
        Guid costCentreId,
        int fiscalYear,
        int month,
        CancellationToken cancellationToken)
    {
        var inicio = new DateOnly(fiscalYear, month, 1);
        var fim = inicio.AddMonths(1);

        // **Não cancelados**, e é a leitura conservadora que BR-8 quer: um
        // pedido em curso já promete o dinheiro. Contar só os executados
        // deixaria passar tudo o que estivesse em aprovação.
        return await context.PaymentRequests
            .AsNoTracking()
            .Where(r => r.CostCentreId == costCentreId
                && r.Status != PaymentRequestStatus.Cancelled
                && r.RequestedOn >= inicio
                && r.RequestedOn < fim)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;
    }

    public async Task<DepartmentCostForecast?> FindForecastAsync(
        Guid forecastId,
        CancellationToken cancellationToken) =>
        await context.CostForecasts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == forecastId, cancellationToken);

    public async Task<DepartmentCostForecast?> FindForecastForUpdateAsync(
        Guid forecastId,
        CancellationToken cancellationToken) =>
        await context.CostForecasts
            .FirstOrDefaultAsync(f => f.Id == forecastId, cancellationToken);

    public async Task<bool> ForecastExistsAsync(
        Guid departmentId,
        int fiscalYear,
        int month,
        CancellationToken cancellationToken) =>
        await context.CostForecasts
            .AsNoTracking()
            .AnyAsync(
                f => f.DepartmentId == departmentId && f.FiscalYear == fiscalYear && f.Month == month,
                cancellationToken);

    public async Task<IReadOnlyList<DepartmentCostForecast>> ListForecastsAsync(
        Guid? departmentId,
        int? fiscalYear,
        CancellationToken cancellationToken)
    {
        var query = context.CostForecasts.AsNoTracking().AsQueryable();

        if (departmentId is { } departamento)
        {
            query = query.Where(f => f.DepartmentId == departamento);
        }

        if (fiscalYear is { } ano)
        {
            query = query.Where(f => f.FiscalYear == ano);
        }

        return await query
            .OrderBy(f => f.FiscalYear)
            .ThenBy(f => f.Month)
            .ToListAsync(cancellationToken);
    }

    public async Task AddForecastAsync(
        DepartmentCostForecast forecast,
        CancellationToken cancellationToken) =>
        await context.CostForecasts.AddAsync(forecast, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
