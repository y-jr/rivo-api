using Rivo.Audit.Contracts;
using Rivo.Projects.Application.Abstractions;

namespace Rivo.Projects.Application.UseCases;

/// <summary>
/// Define o orçamento do projecto, ou revê-o se já existir.
///
/// <para>
/// Distinto do orçamento por centro de custo de `finance` (ADR-040) — a
/// validação cruzada entre os dois continua por desenhar.
/// </para>
/// </summary>
public sealed class SetProjectBudget(IProjectStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<SetProjectBudgetResult> ExecuteAsync(
        Guid projectId,
        decimal amount,
        string currency,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return SetProjectBudgetResult.NotFound();
        }

        try
        {
            projecto.SetBudget(amount, currency, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return SetProjectBudgetResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Projecto fechado, ou revisão a tentar mudar a moeda: conflito
            // com o estado actual, não pedido malformado — 409, não 400.
            return SetProjectBudgetResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.BudgetSet,
                ProjectsAuditEntityTypes.Budget,
                projectId.ToString(),
                context,
                NewValue: $$"""{"amount":{{amount}},"currency":"{{currency.Trim().ToUpperInvariant()}}"}"""),
            cancellationToken);

        return SetProjectBudgetResult.Success();
    }
}

public sealed record SetProjectBudgetResult(SetProjectBudgetOutcome Outcome, string? Error)
{
    public static SetProjectBudgetResult Success() => new(SetProjectBudgetOutcome.Set, null);

    public static SetProjectBudgetResult NotFound() =>
        new(SetProjectBudgetOutcome.NotFound, "Projecto não encontrado.");

    public static SetProjectBudgetResult Rejected(string error) =>
        new(SetProjectBudgetOutcome.Rejected, error);

    public static SetProjectBudgetResult Conflict(string error) =>
        new(SetProjectBudgetOutcome.Conflict, error);
}

public enum SetProjectBudgetOutcome
{
    Set,
    NotFound,

    /// <summary>Pedido malformado — valor não positivo ou moeda inválida. 400.</summary>
    Rejected,

    /// <summary>Projecto fechado, ou revisão a tentar mudar a moeda já fixada. 409.</summary>
    Conflict,
}
