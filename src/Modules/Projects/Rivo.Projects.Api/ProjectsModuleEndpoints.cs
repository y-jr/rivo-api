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

        group.MapPost("/{projectId:guid}/milestones", AddMilestoneAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/milestones/{milestoneId:guid}/reached", ReachMilestoneAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/tasks", AddTaskAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/tasks/{taskId:guid}/assignment", AssignTaskAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/tasks/{taskId:guid}/completion", CompleteTaskAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        // Nunca eliminar — cancelar é o que existe (BR-14).
        group.MapPost("/{projectId:guid}/tasks/{taskId:guid}/cancellation", CancelTaskAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        // Define ou revê o orçamento — o mesmo endpoint serve os dois casos.
        group.MapPost("/{projectId:guid}/budget", SetBudgetAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/allocations", AllocateResourceAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        group.MapPost("/{projectId:guid}/allocations/{allocationId:guid}/end", EndAllocationAsync)
            .RequireAuthorization(ProjectsPermissions.ProjectsWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListProjects listProjects,
        bool? includeClosed,
        CancellationToken cancellationToken)
    {
        var projectos = await listProjects.ExecuteAsync(includeClosed ?? false, cancellationToken);
        return Results.Ok(projectos);
    }

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

    private static async Task<IResult> AddMilestoneAsync(
        Guid projectId,
        AddMilestoneRequest request,
        AddMilestone addMilestone,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await addMilestone.ExecuteAsync(
            projectId, request.Name, request.TargetDate, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AddMilestoneOutcome.Added => Results.Created(
                $"/projects/{projectId}", new { milestoneId = result.MilestoneId }),
            AddMilestoneOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            AddMilestoneOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["marco"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> ReachMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        ReachMilestoneRequest request,
        ReachMilestone reachMilestone,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await reachMilestone.ExecuteAsync(
            projectId, milestoneId, request.ReachedOn, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            ReachMilestoneOutcome.Reached => Results.NoContent(),
            ReachMilestoneOutcome.ProjectNotFound => Results.NotFound(new { erro = "Projecto não encontrado." }),
            ReachMilestoneOutcome.MilestoneNotFound => Results.NotFound(new { erro = "Marco não encontrado." }),
            ReachMilestoneOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível alcançar o marco." }),
            _ => Results.Problem("Resultado inesperado ao alcançar o marco."),
        };
    }

    private static async Task<IResult> AddTaskAsync(
        Guid projectId,
        AddTaskRequest request,
        AddTask addTask,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await addTask.ExecuteAsync(
            projectId, request.Title, request.DueDate, request.AssignedEmployeeId,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AddTaskOutcome.Added => Results.Created($"/projects/{projectId}", new { taskId = result.TaskId }),
            AddTaskOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            AddTaskOutcome.EmployeeNotFound => Results.NotFound(new { erro = result.Error }),
            AddTaskOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["tarefa"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> AssignTaskAsync(
        Guid projectId,
        Guid taskId,
        AssignTaskRequest request,
        AssignTask assignTask,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await assignTask.ExecuteAsync(
            projectId, taskId, request.EmployeeId, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AssignTaskOutcome.Assigned => Results.NoContent(),
            AssignTaskOutcome.ProjectNotFound => Results.NotFound(new { erro = "Projecto não encontrado." }),
            AssignTaskOutcome.TaskNotFound => Results.NotFound(new { erro = "Tarefa não encontrada." }),
            AssignTaskOutcome.EmployeeNotFound => Results.NotFound(new { erro = "Colaborador a atribuir não encontrado." }),
            AssignTaskOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível atribuir a tarefa." }),
            _ => Results.Problem("Resultado inesperado ao atribuir a tarefa."),
        };
    }

    private static async Task<IResult> CompleteTaskAsync(
        Guid projectId,
        Guid taskId,
        CompleteTask completeTask,
        HttpContext http,
        CancellationToken cancellationToken) =>
        TaskLifecycleResult(
            await completeTask.ExecuteAsync(projectId, taskId, BuildAuditContext(http), cancellationToken),
            "concluir");

    private static async Task<IResult> CancelTaskAsync(
        Guid projectId,
        Guid taskId,
        CancelTask cancelTask,
        HttpContext http,
        CancellationToken cancellationToken) =>
        TaskLifecycleResult(
            await cancelTask.ExecuteAsync(projectId, taskId, BuildAuditContext(http), cancellationToken),
            "cancelar");

    private static IResult TaskLifecycleResult(TaskLifecycleOutcome outcome, string acto) => outcome switch
    {
        TaskLifecycleOutcome.Applied => Results.NoContent(),
        TaskLifecycleOutcome.ProjectNotFound => Results.NotFound(new { erro = "Projecto não encontrado." }),
        TaskLifecycleOutcome.TaskNotFound => Results.NotFound(new { erro = "Tarefa não encontrada." }),
        TaskLifecycleOutcome.Rejected => Results.Conflict(new { erro = $"Não foi possível {acto} a tarefa." }),
        _ => Results.Problem($"Resultado inesperado ao {acto} a tarefa."),
    };

    private static async Task<IResult> SetBudgetAsync(
        Guid projectId,
        SetBudgetRequest request,
        SetProjectBudget setBudget,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await setBudget.ExecuteAsync(
            projectId, request.Amount, request.Currency, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            SetProjectBudgetOutcome.Set => Results.NoContent(),
            SetProjectBudgetOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            SetProjectBudgetOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["orcamento"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> AllocateResourceAsync(
        Guid projectId,
        AllocateResourceRequest request,
        AllocateProjectResource allocate,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await allocate.ExecuteAsync(
            projectId, request.Kind, request.ResourceId, request.StartsOn,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AllocateResourceOutcome.Allocated => Results.Created(
                $"/projects/{projectId}", new { allocationId = result.AllocationId }),

            AllocateResourceOutcome.ProjectNotFound or AllocateResourceOutcome.ResourceNotFound =>
                Results.NotFound(new { erro = result.Error }),

            // 409: o mesmo recurso já está alocado, ou o projecto está
            // fechado — conflito com o estado actual, não pedido malformado.
            AllocateResourceOutcome.Conflict =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            // 400: recurso vazio, ou data antes do início do projecto.
            AllocateResourceOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["alocacao"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao alocar o recurso."),
        };
    }

    private static async Task<IResult> EndAllocationAsync(
        Guid projectId,
        Guid allocationId,
        EndAllocationRequest request,
        EndResourceAllocation endAllocation,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await endAllocation.ExecuteAsync(
            projectId, allocationId, request.EndsOn, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            EndAllocationOutcome.Applied => Results.NoContent(),
            EndAllocationOutcome.ProjectNotFound => Results.NotFound(new { erro = "Projecto não encontrado." }),
            EndAllocationOutcome.AllocationNotFound => Results.NotFound(new { erro = "Alocação não encontrada." }),

            // 400: data de fim antes do início da alocação.
            EndAllocationOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["alocacao"] = ["Data de fim inválida."] }),

            // 409: alocação já terminada, ou projecto fechado.
            EndAllocationOutcome.Conflict =>
                Results.Conflict(new { erro = "Não foi possível terminar a alocação." }),

            _ => Results.Problem("Resultado inesperado ao terminar a alocação."),
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

public sealed record AddMilestoneRequest(string Name, DateOnly TargetDate);

public sealed record ReachMilestoneRequest(DateOnly ReachedOn);

public sealed record AddTaskRequest(string Title, DateOnly? DueDate, Guid? AssignedEmployeeId);

public sealed record AssignTaskRequest(Guid? EmployeeId);

public sealed record SetBudgetRequest(decimal Amount, string Currency);

public sealed record AllocateResourceRequest(Rivo.Projects.Domain.ResourceKind Kind, Guid ResourceId, DateOnly StartsOn);

public sealed record EndAllocationRequest(DateOnly EndsOn);
