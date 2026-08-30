using Rivo.Audit.Contracts;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.UseCases;

/// <summary>
/// Vista de leitura de um projecto, com os seus marcos e tarefas.
///
/// <para>
/// A entidade de domínio nunca sai desta camada (architecture/dependency-rules.md
/// §API) — mesma forma de <c>RequisitionView</c> em `procurement`.
/// </para>
/// </summary>
public sealed record ProjectView(
    Guid ProjectId,
    string Name,
    string Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    IReadOnlyList<MilestoneView> Milestones,
    IReadOnlyList<ProjectTaskView> Tasks,
    ProjectBudgetView? Budget);

public sealed record MilestoneView(
    Guid MilestoneId, string Name, string Status, DateOnly TargetDate, DateOnly? ReachedOn);

public sealed record ProjectTaskView(
    Guid TaskId, string Title, string Status, DateOnly? DueDate, Guid? AssignedEmployeeId);

public sealed record ProjectBudgetView(decimal Amount, string Currency, DateTimeOffset SetAt);

internal static class ProjectViews
{
    internal static ProjectView ToView(Project projecto) => new(
        projecto.Id,
        projecto.Name,
        projecto.Status.ToString(),
        projecto.StartDate,
        projecto.EndDate,
        [.. projecto.Milestones.Select(m =>
            new MilestoneView(m.Id, m.Name, m.Status.ToString(), m.TargetDate, m.ReachedOn))],
        [.. projecto.Tasks.Select(t =>
            new ProjectTaskView(t.Id, t.Title, t.Status.ToString(), t.DueDate, t.AssignedEmployeeId))],
        projecto.Budget is { } orcamento
            ? new ProjectBudgetView(orcamento.Amount, orcamento.Currency, orcamento.SetAt)
            : null);
}

public sealed class ListProjects(IProjectStore store)
{
    public async Task<IReadOnlyList<ProjectView>> ExecuteAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var projectos = await store.ListAsync(includeClosed, cancellationToken);
        return [.. projectos.Select(ProjectViews.ToView)];
    }
}

public sealed class GetProject(IProjectStore store)
{
    public async Task<ProjectView?> ExecuteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var projecto = await store.FindAsync(projectId, cancellationToken);
        return projecto is null ? null : ProjectViews.ToView(projecto);
    }
}

public sealed class OpenProject(IProjectStore store, IAuditTrail audit)
{
    public async Task<OpenProjectResult> ExecuteAsync(
        string name,
        DateOnly startDate,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Project projecto;

        try
        {
            projecto = Project.Open(name, startDate);
        }
        catch (ArgumentException error)
        {
            return OpenProjectResult.Rejected(error.Message);
        }

        await store.AddAsync(projecto, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.ProjectOpened,
                ProjectsAuditEntityTypes.Project,
                projecto.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{projecto.Name}}"}"""),
            cancellationToken);

        return OpenProjectResult.Success(projecto.Id);
    }
}

public sealed class CloseProject(IProjectStore store, IAuditTrail audit)
{
    public async Task<CloseProjectOutcome> ExecuteAsync(
        Guid projectId,
        DateOnly endDate,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return CloseProjectOutcome.NotFound;
        }

        try
        {
            projecto.Close(endDate);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return CloseProjectOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.ProjectClosed,
                ProjectsAuditEntityTypes.Project,
                projecto.Id.ToString(),
                context),
            cancellationToken);

        return CloseProjectOutcome.Closed;
    }
}

public sealed record OpenProjectResult(bool Succeeded, Guid? ProjectId, string? Error)
{
    public static OpenProjectResult Success(Guid projectId) => new(true, projectId, null);

    public static OpenProjectResult Rejected(string error) => new(false, null, error);
}

public enum CloseProjectOutcome
{
    Closed,
    NotFound,
    Rejected,
}

public static class ProjectsAuditActions
{
    public const string ProjectOpened = "projects.project.opened";
    public const string ProjectClosed = "projects.project.closed";
    public const string MilestoneAdded = "projects.milestone.added";
    public const string MilestoneReached = "projects.milestone.reached";
    public const string TaskAdded = "projects.task.added";
    public const string TaskAssigned = "projects.task.assigned";
    public const string TaskCompleted = "projects.task.completed";
    public const string TaskCancelled = "projects.task.cancelled";
    public const string BudgetSet = "projects.budget.set";
}

public static class ProjectsAuditEntityTypes
{
    public const string Project = "projects.project";
    public const string Milestone = "projects.milestone";
    public const string Task = "projects.task";
    public const string Budget = "projects.budget";
}
