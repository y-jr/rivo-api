using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Gestão de regras contabilísticas versionadas.
///
/// <para>
/// Uma regra é uma tradução de um evento de negócio (factura, recepção, etc.)
/// para linhas de lançamento. A regra não inventa contas — referencia contas
/// do plano actual, e a validação é feita no momento da definição.
/// </para>
/// </summary>
public sealed class CreateAccountingRule(ILedgerStore store, IAuditTrail audit)
{
    public async Task<CreateAccountingRuleResult> ExecuteAsync(
        string code,
        string name,
        string sourceType,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        IReadOnlyList<AccountingRuleLineInput> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return CreateAccountingRuleResult.Rejected("Uma regra contabilística precisa de linhas.");
        }

        // Normalizar códigos de contas — todas têm de existir.
        var codigos = lines
            .Select(l => (l.AccountCode ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var contas = await store.FindAccountsByCodeAsync(codigos, cancellationToken);

        foreach (var codigo in codigos)
        {
            if (!contas.TryGetValue(codigo, out var conta))
            {
                return CreateAccountingRuleResult.AccountNotFound(
                    $"A conta '{codigo}' não existe no plano.");
            }

            // Uma regra que lançasse numa agregadora produziria lançamentos
            // inválidos para sempre — só se descobriria à primeira emissão.
            if (!conta.AcceptsPostings)
            {
                return CreateAccountingRuleResult.Rejected(
                    $"A conta {conta.Code} é {conta.Category} — agregadora, não de movimento. " +
                    "Uma regra que lançasse nela partiria a hierarquia.");
            }
        }

        // Tentar criar a regra — pode falhar por validação de balanço.
        AccountingRule regra;

        try
        {
            regra = AccountingRule.Create(
                code,
                name,
                sourceType,
                source,
                effectiveFrom,
                effectiveTo,
                [.. lines.Select(l => new AccountingRuleLine(
                    l.AccountCode, l.Side, l.Amount, l.Description))]);
        }
        catch (ArgumentException error)
        {
            return CreateAccountingRuleResult.Rejected(error.Message);
        }

        await store.AddAccountingRuleAsync(regra, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountingRuleCreated,
                FinanceAuditEntityTypes.AccountingRule,
                regra.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{regra.Code}}","name":"{{regra.Name}}","sourceType":"{{regra.SourceType}}","lines":{{regra.Lines.Count}}}"""),
            cancellationToken);

        return CreateAccountingRuleResult.Success(regra.Id);
    }
}

public sealed record CreateAccountingRuleResult(
    CreateAccountingRuleOutcome Outcome,
    Guid? RuleId,
    string? Error)
{
    public static CreateAccountingRuleResult Success(Guid id) =>
        new(CreateAccountingRuleOutcome.Created, id, null);

    public static CreateAccountingRuleResult AccountNotFound(string error) =>
        new(CreateAccountingRuleOutcome.AccountNotFound, null, error);

    public static CreateAccountingRuleResult Rejected(string error) =>
        new(CreateAccountingRuleOutcome.Rejected, null, error);
}

public enum CreateAccountingRuleOutcome
{
    Created,

    /// <summary>Uma conta referenciada não existe — 404.</summary>
    AccountNotFound,

    Rejected,
}

public sealed class ListAccountingRules(ILedgerStore store)
{
    public async Task<IReadOnlyList<AccountingRuleView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var regras = await store.ListAccountingRulesAsync(includeInactive, cancellationToken);

        return [.. regras.Select(r => new AccountingRuleView(
            r.Id,
            r.Code,
            r.Name,
            r.SourceType,
            r.Source,
            r.EffectiveFrom,
            r.EffectiveTo,
            r.IsActive,
            [.. r.Lines.OrderBy(l => l.AccountCode).Select(l =>
                new AccountingRuleLineView(l.AccountCode, l.Side.ToString(), l.Amount.ToString(), l.Description))]))];
    }
}

public sealed record AccountingRuleView(
    Guid RuleId,
    string Code,
    string Name,
    string SourceType,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<AccountingRuleLineView> Lines);

public sealed record AccountingRuleLineView(
    string AccountCode,
    string Side,
    string Amount,
    string Description);

public sealed record AccountingRuleLineInput(
    string AccountCode,
    EntrySide Side,
    PostingAmount Amount,
    string Description);

/// <summary>
/// Desactiva uma regra contabilística. Os lançamentos que ela produziu ficam —
/// o que pára é a tradução dos documentos futuros.
/// </summary>
public sealed class DeactivateAccountingRule(ILedgerStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid ruleId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var regra = await store.FindAccountingRuleAsync(ruleId, cancellationToken);

        if (regra is null)
        {
            return false;
        }

        regra.Deactivate();
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountingRuleDeactivated,
                FinanceAuditEntityTypes.AccountingRule,
                regra.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{regra.Code}}","isActive":false}"""),
            cancellationToken);

        return true;
    }
}
