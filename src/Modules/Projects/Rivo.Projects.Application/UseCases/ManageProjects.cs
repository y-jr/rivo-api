using Rivo.Audit.Contracts;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.UseCases;

public sealed class ListProjects(IProjectStore store)
{
    public async Task<IReadOnlyList<Project>> ExecuteAsync(bool includeClosed, CancellationToken cancellationToken) =>
        await store.ListAsync(includeClosed, cancellationToken);
}

public sealed class GetProject(IProjectStore store)
{
    public Task<Project?> ExecuteAsync(Guid projectId, CancellationToken cancellationToken) =>
        store.FindAsync(projectId, cancellationToken);
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
}

public static class ProjectsAuditEntityTypes
{
    public const string Project = "projects.project";
}
