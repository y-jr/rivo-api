using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.UseCases;
using Rivo.Inventory.Contracts;

namespace Rivo.Inventory.Api;

public static class InventoryModuleEndpoints
{
    public static IEndpointRouteBuilder MapInventoryModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/inventory");

        group.MapGet("/items", ListAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapGet("/items/{itemId:guid}", GetAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapPost("/items", RegisterAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        // Desactivar, nunca eliminar — pode estar referenciado por recepções.
        group.MapPost("/items/{itemId:guid}/status", SetStatusAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListInventoryItems listItems,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await listItems.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid itemId,
        GetInventoryItem getItem,
        CancellationToken cancellationToken)
    {
        var item = await getItem.ExecuteAsync(itemId, cancellationToken);

        return item is null
            ? Results.NotFound(new { erro = "Item não encontrado." })
            : Results.Ok(item);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterItemRequest request,
        RegisterInventoryItem registerItem,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await registerItem.ExecuteAsync(
            request.Sku, request.Name, request.Unit, BuildAuditContext(http), cancellationToken);

        if (result.Succeeded)
        {
            return Results.Created($"/inventory/items/{result.ItemId}", new { itemId = result.ItemId });
        }

        return result.ItemId is not null
            ? Results.Conflict(new { erro = result.Error, itemId = result.ItemId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["item"] = [result.Error!] });
    }

    private static async Task<IResult> SetStatusAsync(
        Guid itemId,
        SetItemStatusRequest request,
        SetInventoryItemStatus setStatus,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var encontrado = await setStatus.ExecuteAsync(
            itemId, request.Active, BuildAuditContext(http), cancellationToken);

        return encontrado
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Item não encontrado." });
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

public sealed record RegisterItemRequest(string Sku, string Name, string Unit);

public sealed record SetItemStatusRequest(bool Active);
