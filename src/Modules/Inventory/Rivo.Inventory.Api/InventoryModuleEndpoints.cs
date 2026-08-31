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

        group.MapPost("/items/{itemId:guid}/movements/receipts", RegisterReceiptAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapPost("/items/{itemId:guid}/movements/issues", RegisterIssueAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapPost("/items/{itemId:guid}/movements/adjustments", RegisterAdjustmentAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapPost("/items/{itemId:guid}/movements/transfers", TransferAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapGet("/warehouses", ListWarehousesAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapGet("/warehouses/{warehouseId:guid}", GetWarehouseAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapPost("/warehouses", RegisterWarehouseAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        // Desactivar, nunca eliminar — pode estar referenciado por movimentos.
        group.MapPost("/warehouses/{warehouseId:guid}/status", SetWarehouseStatusAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapGet("/counts", ListCountsAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapGet("/counts/{countId:guid}", GetCountAsync)
            .RequireAuthorization(InventoryPermissions.ItemsRead);

        group.MapPost("/counts", OpenCountAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapPost("/counts/{countId:guid}/lines", AddCountLineAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        group.MapPost("/counts/{countId:guid}/close", CloseCountAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        // Nunca DELETE (BR-14) — cancelar é o que existe para um engano.
        group.MapPost("/counts/{countId:guid}/cancellation", CancelCountAsync)
            .RequireAuthorization(InventoryPermissions.ItemsWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListInventoryItems listItems,
        bool? includeInactive,
        CancellationToken cancellationToken)
    {
        var itens = await listItems.ExecuteAsync(includeInactive ?? false, cancellationToken);
        return Results.Ok(itens);
    }

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

    private static async Task<IResult> RegisterReceiptAsync(
        Guid itemId,
        RegisterMovementRequest request,
        RegisterReceipt registerReceipt,
        HttpContext http,
        CancellationToken cancellationToken) =>
        MovementResult(await registerReceipt.ExecuteAsync(
            itemId, request.WarehouseId, request.Quantity, request.Reason, request.OccurredOn,
            BuildAuditContext(http), cancellationToken),
            itemId, "recepcao");

    private static async Task<IResult> RegisterIssueAsync(
        Guid itemId,
        RegisterMovementRequest request,
        RegisterIssue registerIssue,
        HttpContext http,
        CancellationToken cancellationToken) =>
        MovementResult(await registerIssue.ExecuteAsync(
            itemId, request.WarehouseId, request.Quantity, request.Reason, request.OccurredOn,
            BuildAuditContext(http), cancellationToken),
            itemId, "saida");

    private static async Task<IResult> RegisterAdjustmentAsync(
        Guid itemId,
        RegisterAdjustmentRequest request,
        RegisterAdjustment registerAdjustment,
        HttpContext http,
        CancellationToken cancellationToken) =>
        MovementResult(await registerAdjustment.ExecuteAsync(
            itemId, request.WarehouseId, request.QuantityDelta, request.Reason, request.OccurredOn,
            BuildAuditContext(http), cancellationToken),
            itemId, "ajuste");

    private static async Task<IResult> TransferAsync(
        Guid itemId,
        TransferStockRequest request,
        TransferStock transferStock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await transferStock.ExecuteAsync(
            itemId, request.FromWarehouseId, request.ToWarehouseId, request.Quantity, request.Reason,
            request.OccurredOn, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            TransferOutcome.Registered => Results.Created(
                $"/inventory/items/{itemId}",
                new
                {
                    outMovementId = result.OutMovementId,
                    inMovementId = result.InMovementId,
                    quantityAtSource = result.QuantityAtSource,
                    quantityAtDestination = result.QuantityAtDestination,
                }),
            TransferOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            TransferOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["transferencia"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> ListWarehousesAsync(
        ListWarehouses listWarehouses,
        bool? includeInactive,
        CancellationToken cancellationToken)
    {
        var armazens = await listWarehouses.ExecuteAsync(includeInactive ?? false, cancellationToken);
        return Results.Ok(armazens);
    }

    private static async Task<IResult> GetWarehouseAsync(
        Guid warehouseId,
        GetWarehouse getWarehouse,
        CancellationToken cancellationToken)
    {
        var armazem = await getWarehouse.ExecuteAsync(warehouseId, cancellationToken);

        return armazem is null
            ? Results.NotFound(new { erro = "Armazém não encontrado." })
            : Results.Ok(armazem);
    }

    private static async Task<IResult> RegisterWarehouseAsync(
        RegisterWarehouseRequest request,
        RegisterWarehouse registerWarehouse,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await registerWarehouse.ExecuteAsync(
            request.Code, request.Name, BuildAuditContext(http), cancellationToken);

        if (result.Succeeded)
        {
            return Results.Created($"/inventory/warehouses/{result.WarehouseId}", new { warehouseId = result.WarehouseId });
        }

        return result.WarehouseId is not null
            ? Results.Conflict(new { erro = result.Error, warehouseId = result.WarehouseId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["warehouse"] = [result.Error!] });
    }

    private static async Task<IResult> SetWarehouseStatusAsync(
        Guid warehouseId,
        SetWarehouseStatusRequest request,
        SetWarehouseStatus setStatus,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var encontrado = await setStatus.ExecuteAsync(
            warehouseId, request.Active, BuildAuditContext(http), cancellationToken);

        return encontrado
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Armazém não encontrado." });
    }

    private static async Task<IResult> ListCountsAsync(
        ListInventoryCounts listCounts,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var contagens = await listCounts.ExecuteAsync(warehouseId, cancellationToken);
        return Results.Ok(contagens);
    }

    private static async Task<IResult> GetCountAsync(
        Guid countId,
        GetInventoryCount getCount,
        CancellationToken cancellationToken)
    {
        var contagem = await getCount.ExecuteAsync(countId, cancellationToken);

        return contagem is null
            ? Results.NotFound(new { erro = "Contagem não encontrada." })
            : Results.Ok(contagem);
    }

    private static async Task<IResult> OpenCountAsync(
        OpenCountRequest request,
        OpenInventoryCount openCount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openCount.ExecuteAsync(
            request.WarehouseId, request.OccurredOn, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenCountOutcome.Opened => Results.Created($"/inventory/counts/{result.CountId}", new { countId = result.CountId }),
            OpenCountOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            OpenCountOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["count"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> AddCountLineAsync(
        Guid countId,
        AddCountLineRequest request,
        AddInventoryCountLine addLine,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await addLine.ExecuteAsync(
            countId, request.ItemId, request.CountedQuantity, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AddCountLineOutcome.Added => Results.Created(
                $"/inventory/counts/{countId}",
                new
                {
                    lineId = result.LineId,
                    expectedQuantity = result.ExpectedQuantity,
                    countedQuantity = result.CountedQuantity,
                    variance = result.Variance,
                }),
            AddCountLineOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            AddCountLineOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["line"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> CloseCountAsync(
        Guid countId,
        CloseInventoryCount closeCount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await closeCount.ExecuteAsync(countId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CloseCountOutcome.Closed => Results.Ok(new { generatedAdjustmentIds = result.GeneratedAdjustmentIds }),
            CloseCountOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            _ => Results.Conflict(new { erro = result.Error }),
        };
    }

    private static async Task<IResult> CancelCountAsync(
        Guid countId,
        CancelCountRequest request,
        CancelInventoryCount cancelCount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancelCount.ExecuteAsync(countId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CancelCountOutcome.Cancelled => Results.NoContent(),
            CancelCountOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            CancelCountOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [result.Error!] }),
        };
    }

    private static IResult MovementResult(RegisterMovementResult result, Guid itemId, string campo) =>
        result.Outcome switch
        {
            RegisterMovementOutcome.Registered => Results.Created(
                $"/inventory/items/{itemId}",
                new
                {
                    movementId = result.MovementId,
                    quantityOnHand = result.QuantityOnHand,
                    quantityAtWarehouse = result.QuantityAtWarehouse,
                }),
            RegisterMovementOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            RegisterMovementOutcome.Conflict => Results.Conflict(new { erro = result.Error }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { [campo] = [result.Error!] }),
        };

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

public sealed record RegisterMovementRequest(Guid WarehouseId, decimal Quantity, string? Reason, DateOnly OccurredOn);

public sealed record RegisterAdjustmentRequest(Guid WarehouseId, decimal QuantityDelta, string Reason, DateOnly OccurredOn);

public sealed record TransferStockRequest(
    Guid FromWarehouseId, Guid ToWarehouseId, decimal Quantity, string? Reason, DateOnly OccurredOn);

public sealed record RegisterWarehouseRequest(string Code, string Name);

public sealed record SetWarehouseStatusRequest(bool Active);

public sealed record OpenCountRequest(Guid WarehouseId, DateOnly OccurredOn);

public sealed record AddCountLineRequest(Guid ItemId, decimal CountedQuantity);

public sealed record CancelCountRequest(string Reason);
