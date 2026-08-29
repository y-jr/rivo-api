using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Contracts;

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

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance", SetMaintenanceAsync)
            .RequireAuthorization(FleetPermissions.VehiclesWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListVehicles listVehicles,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await listVehicles.ExecuteAsync(includeInactive ?? false, cancellationToken));

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

    private static async Task<IResult> SetMaintenanceAsync(
        Guid vehicleId,
        SetMaintenanceRequest request,
        SetVehicleMaintenance setMaintenance,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await setMaintenance.ExecuteAsync(
            vehicleId, request.InMaintenance, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            SetMaintenanceOutcome.Applied => Results.NoContent(),
            SetMaintenanceOutcome.NotFound => Results.NotFound(new { erro = "Viatura não encontrada." }),
            SetMaintenanceOutcome.Rejected => Results.Conflict(new { erro = "Transição de estado inválida." }),
            _ => Results.Problem("Resultado inesperado ao alterar o estado da viatura."),
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

public sealed record RegisterVehicleRequest(string PlateNumber, string Model);

public sealed record SetMaintenanceRequest(bool InMaintenance);
