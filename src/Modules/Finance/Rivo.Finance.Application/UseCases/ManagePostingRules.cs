using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Define como um documento se traduz em lançamento.
///
/// <para>
/// <strong>A regra é verificada contra o plano no momento em que se define</strong>,
/// e não a cada postagem. Uma conta que não existe, ou que é agregadora, é
/// recusada aqui — onde quem a corrige é quem a está a configurar, e não o
/// vendedor a tentar emitir uma factura três semanas depois.
/// </para>
/// </summary>
public sealed class DefinePostingRule(ILedgerStore store, IAuditTrail audit)
{
    public async Task<DefinePostingRuleResult> ExecuteAsync(
        PostingEvent postingEvent,
        string journalCode,
        string description,
        IReadOnlyList<PostingRuleLineInput> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return DefinePostingRuleResult.Rejected("Uma regra de postagem tem linhas.");
        }

        // Uma activa por acontecimento: duas tornariam a tradução ambígua.
        if (await store.FindActivePostingRuleAsync(postingEvent, cancellationToken) is not null)
        {
            return DefinePostingRuleResult.Duplicate(
                $"Já existe regra activa para {postingEvent}. Desactive-a antes de definir outra.");
        }

        var diario = await store.FindJournalByCodeAsync(
            (journalCode ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);

        if (diario is null)
        {
            return DefinePostingRuleResult.JournalNotFound();
        }

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
                return DefinePostingRuleResult.AccountNotFound(
                    $"A conta '{codigo}' não existe no plano.");
            }

            // Uma regra que lançasse numa agregadora produziria lançamentos
            // inválidos para sempre — e só se descobriria à primeira emissão.
            if (!conta.AcceptsPostings)
            {
                return DefinePostingRuleResult.Rejected(
                    $"A conta {conta.Code} é {conta.Category} — agregadora, não de movimento. " +
                    "Uma regra que lançasse nela partiria a hierarquia a cada documento.");
            }
        }

        PostingRule regra;

        try
        {
            regra = PostingRule.Define(
                postingEvent,
                diario.Code,
                description,
                [.. lines.Select(l => new NewPostingRuleLine(
                    l.AccountCode, l.Side, l.Amount, l.Description))]);
        }
        catch (UnbalancedPostingRuleException error)
        {
            return DefinePostingRuleResult.Unbalanced(error.Message);
        }
        catch (ArgumentException error)
        {
            return DefinePostingRuleResult.Rejected(error.Message);
        }

        await store.AddPostingRuleAsync(regra, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PostingRuleDefined,
                FinanceAuditEntityTypes.PostingRule,
                regra.Id.ToString(),
                context,
                NewValue: $$"""{"event":"{{regra.Event}}","journal":"{{regra.JournalCode}}","lines":{{regra.Lines.Count}}}"""),
            cancellationToken);

        return DefinePostingRuleResult.Success(regra.Id);
    }
}

public sealed record PostingRuleLineInput(
    string AccountCode,
    EntrySide Side,
    PostingAmount Amount,
    string Description);

public sealed record DefinePostingRuleResult(
    DefinePostingRuleOutcome Outcome,
    Guid? RuleId,
    string? Error)
{
    public static DefinePostingRuleResult Success(Guid id) =>
        new(DefinePostingRuleOutcome.Defined, id, null);

    public static DefinePostingRuleResult Duplicate(string error) =>
        new(DefinePostingRuleOutcome.Duplicate, null, error);

    public static DefinePostingRuleResult JournalNotFound() =>
        new(DefinePostingRuleOutcome.JournalNotFound, null, null);

    public static DefinePostingRuleResult AccountNotFound(string error) =>
        new(DefinePostingRuleOutcome.AccountNotFound, null, error);

    public static DefinePostingRuleResult Unbalanced(string error) =>
        new(DefinePostingRuleOutcome.Unbalanced, null, error);

    public static DefinePostingRuleResult Rejected(string error) =>
        new(DefinePostingRuleOutcome.Rejected, null, error);
}

public enum DefinePostingRuleOutcome
{
    Defined,

    /// <summary>Já há regra activa para o acontecimento — 409.</summary>
    Duplicate,

    JournalNotFound,
    AccountNotFound,

    /// <summary>
    /// A regra não equilibra <strong>enquanto expressão</strong> — 400, com
    /// razão própria. É a mesma distinção que a partida dobrada faz num
    /// lançamento, um nível acima.
    /// </summary>
    Unbalanced,

    Rejected,
}

public sealed class ListPostingRules(ILedgerStore store)
{
    public async Task<IReadOnlyList<PostingRuleView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var regras = await store.ListPostingRulesAsync(includeInactive, cancellationToken);

        return [.. regras.Select(r => new PostingRuleView(
            r.Id,
            r.Event.ToString(),
            r.JournalCode,
            r.Description,
            r.IsActive,
            [.. r.Lines.OrderBy(l => l.LineNumber).Select(l => new PostingRuleLineView(
                l.LineNumber, l.AccountCode, l.Side.ToString(), l.Amount.ToString(), l.Description))]))];
    }
}

public sealed record PostingRuleView(
    Guid RuleId,
    string Event,
    string JournalCode,
    string Description,
    bool IsActive,
    IReadOnlyList<PostingRuleLineView> Lines);

public sealed record PostingRuleLineView(
    int LineNumber,
    string AccountCode,
    string Side,
    string Amount,
    string Description);

/// <summary>
/// Desactiva uma regra. Os lançamentos que ela produziu ficam — o que pára é a
/// tradução dos documentos futuros.
/// </summary>
public sealed class DeactivatePostingRule(ILedgerStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid ruleId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var regra = await store.FindPostingRuleForUpdateAsync(ruleId, cancellationToken);

        if (regra is null)
        {
            return false;
        }

        regra.Deactivate();
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PostingRuleDeactivated,
                FinanceAuditEntityTypes.PostingRule,
                regra.Id.ToString(),
                context,
                NewValue: $$"""{"event":"{{regra.Event}}","isActive":false}"""),
            cancellationToken);

        return true;
    }
}
