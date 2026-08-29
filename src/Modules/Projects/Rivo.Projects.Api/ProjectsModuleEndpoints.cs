using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Projects.Application.UseCases;
using Rivo.Projects.Contracts;

namespace Rivo.Projects.Api;

public static class ProjectsModuleEndpoints
{
    public static IEndpointRouteBuilder MapProjectsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/projects");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsRead);

        group.MapGet("/{projectId:guid}", GetAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsRead);

        group.MapPost("/", OpenAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        // Nunca eliminar — fechar é o que existe.
        group.MapPost("/{projectId:guid}/closure", CloseAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListProjects listProjects,
        bool? includeClosed,
        CancellationToken cancellationToken) =>
        Results.Ok(await listProjects.ExecuteAsync(includeClosed ?? false, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid projectId,
        GetProject getProject,
        CancellationToken cancellationToken)
    {
        var projecto = await getProject.ExecuteAsync(projectId, cancellationToken);

        return projecto is null
            ? Results.NotFound(new { erro = "Projecto não encontrado." })
            : Results.Ok(projecto);
    }

    private static async Task<IResult> OpenAsync(
        OpenProjectRequest request,
        OpenProject openProject,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openProject.ExecuteAsync(
            request.Name, request.StartDate, BuildAuditContext(http), cancellationToken);

        return result.Succeeded
            ? Results.Created($"/projects/{result.ProjectId}", new { projectId = result.ProjectId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["projecto"] = [result.Error!] });
    }

    private static async Task<IResult> CloseAsync(
        Guid projectId,
        CloseProjectRequest request,
        CloseProject closeProject,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await closeProject.ExecuteAsync(
            projectId, request.EndDate, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            CloseProjectOutcome.Closed => Results.NoContent(),
            CloseProjectOutcome.NotFound => Results.NotFound(new { erro = "Projecto não encontrado." }),
            CloseProjectOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível fechar o projecto." }),
            _ => Results.Problem("Resultado inesperado ao fechar o projecto."),
        };
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }
}

public sealed record OpenProjectRequest(string Name, DateOnly StartDate);

public sealed record CloseProjectRequest(DateOnly EndDate);
