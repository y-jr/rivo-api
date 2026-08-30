using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Contracts;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Api;

public static class FleetModuleEndpoints
{
    public static IEndpointRouteBuilder MapFleetModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/fleet");

        group.MapGet("/vehicles", ListAsync)
            .RequireAuthorization(FleetPermissions.VehiclesRead);

        group.MapGet("/vehicles/{vehicleId:guid}", GetAsync)
            .RequireAuthorization(FleetPermissions.VehiclesRead);

        group.MapPost("/vehicles", RegisterAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        // Nunca eliminar — desactivar é o que existe.
        group.MapPost("/vehicles/{vehicleId:guid}/deactivation", DeactivateAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance", OpenMaintenanceAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance/{maintenanceId:guid}/closure", CloseMaintenanceAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/assignments", AssignAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/assignments/{assignmentId:guid}/closure", EndAssignmentAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance-plans", SchedulePlanAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance-plans/{planId:guid}/cycles", CompletePlanCycleAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        // Nunca eliminar — cancelar é o que existe.
        group.MapPost("/vehicles/{vehicleId:guid}/maintenance-plans/{planId:guid}/cancellation", CancelPlanAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        // O alerta: viaturas com plano devido, sem esperar por uma viatura
        // concreta — por isso vive fora de /vehicles/{id}.
        group.MapGet("/maintenance-plans/due", ListDuePlansAsync)
            .RequireAuthorization(FleetPermissions.VehiclesRead);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListVehicles listVehicles,
        bool? includeInactive,
        CancellationToken cancellationToken)
    {
        var veiculos = await listVehicles.ExecuteAsync(includeInactive ?? false, cancellationToken);
        return Results.Ok(veiculos);
    }

    private static async Task<IResult> GetAsync(
        Guid vehicleId,
        GetVehicle getVehicle,
        CancellationToken cancellationToken)
    {
        var veiculo = await getVehicle.ExecuteAsync(vehicleId, cancellationToken);

        return veiculo is null
            ? Results.NotFound(new { erro = "Viatura não encontrada." })
            : Results.Ok(veiculo);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterVehicleRequest request,
        RegisterVehicle registerVehicle,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await registerVehicle.ExecuteAsync(
            request.PlateNumber, request.Model, BuildAuditContext(http), cancellationToken);

        if (result.Succeeded)
        {
            return Results.Created($"/fleet/vehicles/{result.VehicleId}", new { vehicleId = result.VehicleId });
        }

        return result.VehicleId is not null
            ? Results.Conflict(new { erro = result.Error, vehicleId = result.VehicleId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["viatura"] = [result.Error!] });
    }

    private static async Task<IResult> DeactivateAsync(
        Guid vehicleId,
        DeactivateVehicle deactivateVehicle,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var encontrada = await deactivateVehicle.ExecuteAsync(vehicleId, BuildAuditContext(http), cancellationToken);

        return encontrada
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Viatura não encontrada." });
    }

    private static async Task<IResult> OpenMaintenanceAsync(
        Guid vehicleId,
        OpenMaintenanceRequest request,
        OpenMaintenance openMaintenance,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MaintenanceType>(request.Type, ignoreCase: true, out var tipo))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tipo"] = [$"Tipo de manutenção desconhecido: '{request.Type}'. Use Preventive ou Corrective."],
            });
        }

        var result = await openMaintenance.ExecuteAsync(
            vehicleId, tipo, request.Description, request.StartedOn, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenMaintenanceOutcome.Opened => Results.Created(
                $"/fleet/vehicles/{vehicleId}", new { maintenanceId = result.MaintenanceId }),
            OpenMaintenanceOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            OpenMaintenanceOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["manutencao"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> CloseMaintenanceAsync(
        Guid vehicleId,
        Guid maintenanceId,
        CloseMaintenanceRequest request,
        CloseMaintenance closeMaintenance,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await closeMaintenance.ExecuteAsync(
            vehicleId, maintenanceId, request.EndedOn, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            MaintenanceLifecycleOutcome.Closed => Results.NoContent(),
            MaintenanceLifecycleOutcome.VehicleNotFound => Results.NotFound(new { erro = "Viatura não encontrada." }),
            MaintenanceLifecycleOutcome.MaintenanceNotFound => Results.NotFound(new { erro = "Registo de manutenção não encontrado." }),
            MaintenanceLifecycleOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível fechar a manutenção." }),
            _ => Results.Problem("Resultado inesperado ao fechar a manutenção."),
        };
    }

    private static async Task<IResult> AssignAsync(
        Guid vehicleId,
        AssignVehicleRequest request,
        AssignVehicle assignVehicle,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await assignVehicle.ExecuteAsync(
            vehicleId, request.EmployeeId, request.StartedOn, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AssignVehicleOutcome.Assigned => Results.Created(
                $"/fleet/vehicles/{vehicleId}", new { assignmentId = result.AssignmentId }),
            AssignVehicleOutcome.VehicleNotFound => Results.NotFound(new { erro = result.Error }),
            AssignVehicleOutcome.EmployeeNotFound => Results.NotFound(new { erro = result.Error }),
            AssignVehicleOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["atribuicao"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> EndAssignmentAsync(
        Guid vehicleId,
        Guid assignmentId,
        EndAssignmentRequest request,
        EndVehicleAssignment endAssignment,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await endAssignment.ExecuteAsync(
            vehicleId, assignmentId, request.EndedOn, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AssignmentLifecycleOutcome.Ended => Results.NoContent(),
            AssignmentLifecycleOutcome.VehicleNotFound => Results.NotFound(new { erro = "Viatura não encontrada." }),
            AssignmentLifecycleOutcome.AssignmentNotFound => Results.NotFound(new { erro = "Atribuição não encontrada." }),
            AssignmentLifecycleOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível terminar a atribuição." }),
            _ => Results.Problem("Resultado inesperado ao terminar a atribuição."),
        };
    }

    private static async Task<IResult> SchedulePlanAsync(
        Guid vehicleId,
        SchedulePlanRequest request,
        SchedulePlan schedulePlan,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await schedulePlan.ExecuteAsync(
            vehicleId, request.Description, request.IntervalDays, request.FirstDueOn,
            BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            SchedulePlanOutcome.Scheduled => Results.Created(
                $"/fleet/vehicles/{vehicleId}", new { planId = result.PlanId }),
            SchedulePlanOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            SchedulePlanOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["plano"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> CompletePlanCycleAsync(
        Guid vehicleId,
        Guid planId,
        CompletePlanCycleRequest request,
        CompletePlanCycle completePlanCycle,
        HttpContext http,
        CancellationToken cancellationToken) =>
        PlanLifecycleResult(await completePlanCycle.ExecuteAsync(
            vehicleId, planId, request.CompletedOn, BuildAuditContext(http), cancellationToken),
            "concluir o ciclo do plano");

    private static async Task<IResult> CancelPlanAsync(
        Guid vehicleId,
        Guid planId,
        CancelPlan cancelPlan,
        HttpContext http,
        CancellationToken cancellationToken) =>
        PlanLifecycleResult(await cancelPlan.ExecuteAsync(
            vehicleId, planId, BuildAuditContext(http), cancellationToken),
            "cancelar o plano");

    private static IResult PlanLifecycleResult(PlanLifecycleOutcome outcome, string acto) => outcome switch
    {
        PlanLifecycleOutcome.Applied => Results.NoContent(),
        PlanLifecycleOutcome.VehicleNotFound => Results.NotFound(new { erro = "Viatura não encontrada." }),
        PlanLifecycleOutcome.PlanNotFound => Results.NotFound(new { erro = "Plano de manutenção não encontrado." }),
        PlanLifecycleOutcome.Rejected => Results.Conflict(new { erro = $"Não foi possível {acto}." }),
        _ => Results.Problem($"Resultado inesperado ao {acto}."),
    };

    private static async Task<IResult> ListDuePlansAsync(
        ListDueMaintenancePlans listDuePlans,
        int? withinDays,
        CancellationToken cancellationToken)
    {
        var planos = await listDuePlans.ExecuteAsync(withinDays ?? 0, cancellationToken);
        return Results.Ok(planos);
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

public sealed record RegisterVehicleRequest(string PlateNumber, string Model);

public sealed record OpenMaintenanceRequest(string Type, string Description, DateOnly StartedOn);

public sealed record CloseMaintenanceRequest(DateOnly EndedOn);

public sealed record AssignVehicleRequest(Guid EmployeeId, DateOnly StartedOn);

public sealed record EndAssignmentRequest(DateOnly EndedOn);

public sealed record SchedulePlanRequest(string Description, int IntervalDays, DateOnly FirstDueOn);

public sealed record CompletePlanCycleRequest(DateOnly CompletedOn);
