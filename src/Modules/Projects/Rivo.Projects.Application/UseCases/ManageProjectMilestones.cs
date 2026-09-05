using Rivo.Audit.Contracts;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.UseCases;

public sealed class AddMilestone(IProjectStore store, IAuditTrail audit)
{
    public async Task<AddMilestoneResult> ExecuteAsync(
        Guid projectId,
        string name,
        DateOnly targetDate,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return AddMilestoneResult.NotFound();
        }

        Milestone marco;

        try
        {
            marco = projecto.AddMilestone(name, targetDate);
        }
        catch (ArgumentException error)
        {
            return AddMilestoneResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Projecto fechado: conflito com o estado actual, não pedido
            // malformado — 409, não 400.
            return AddMilestoneResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.MilestoneAdded,
                ProjectsAuditEntityTypes.Milestone,
                marco.Id.ToString(),
                context,
                NewValue: $$"""{"projectId":"{{projectId}}","name":"{{marco.Name}}","targetDate":"{{marco.TargetDate:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return AddMilestoneResult.Success(marco.Id);
    }
}

public sealed record AddMilestoneResult(AddMilestoneOutcome Outcome, Guid? MilestoneId, string? Error)
{
    public static AddMilestoneResult Success(Guid milestoneId) =>
        new(AddMilestoneOutcome.Added, milestoneId, null);

    public static AddMilestoneResult NotFound() =>
        new(AddMilestoneOutcome.NotFound, null, "Projecto não encontrado.");

    public static AddMilestoneResult Rejected(string error) =>
        new(AddMilestoneOutcome.Rejected, null, error);

    public static AddMilestoneResult Conflict(string error) =>
        new(AddMilestoneOutcome.Conflict, null, error);
}

public enum AddMilestoneOutcome
{
    Added,
    NotFound,

    /// <summary>Pedido malformado — nome vazio ou data antes do início. 400.</summary>
    Rejected,

    /// <summary>Projecto fechado. 409.</summary>
    Conflict,
}

/// <summary>Marca um marco como alcançado. Vale uma vez só (ver <see cref="Milestone.Reach"/>).</summary>
public sealed class ReachMilestone(IProjectStore store, IAuditTrail audit)
{
    public async Task<ReachMilestoneOutcome> ExecuteAsync(
        Guid projectId,
        Guid milestoneId,
        DateOnly reachedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return ReachMilestoneOutcome.ProjectNotFound;
        }

        // Verificado aqui, e não apanhado por excepção do domínio: distinguir
        // "marco inexistente" de "marco já alcançado" exige que quem chama
        // veja a colecção, porque as duas violam o mesmo tipo de invariante
        // do lado do agregado.
        if (projecto.Milestones.All(m => m.Id != milestoneId))
        {
            return ReachMilestoneOutcome.MilestoneNotFound;
        }

        try
        {
            projecto.ReachMilestone(milestoneId, reachedOn);
        }
        catch (InvalidOperationException)
        {
            return ReachMilestoneOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.MilestoneReached,
                ProjectsAuditEntityTypes.Milestone,
                milestoneId.ToString(),
                context,
                NewValue: $$"""{"reachedOn":"{{reachedOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return ReachMilestoneOutcome.Reached;
    }
}

public enum ReachMilestoneOutcome
{
    Reached,
    ProjectNotFound,
    MilestoneNotFound,

    /// <summary>Já tinha sido alcançado antes, ou o projecto está fechado.</summary>
    Rejected,
}
