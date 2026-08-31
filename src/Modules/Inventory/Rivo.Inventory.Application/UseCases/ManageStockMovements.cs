using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Confirma que o armazém existe e está activo antes de qualquer movimento.
/// Recusa, não omissão (`modules/inventory.md`): sem armazém válido não há
/// movimento, mesma disciplina de <c>ISubsidyExemptionDetermination</c> em
/// `fiscal` para dado obrigatório em falta.
///
/// <para>
/// Distingue "não encontrado" (404 — mesmo tratamento de item inexistente)
/// de "inactivo" (409 — existe, mas não está utilizável, mesma semântica de
/// <see cref="InventoryItem.EnsureActive"/>).
/// </para>
/// </summary>
internal enum WarehouseUsability
{
    Usable,
    NotFound,
    Inactive,
}

internal static class WarehouseGuard
{
    internal static async Task<WarehouseUsability> CheckAsync(
        IWarehouseStore store, Guid warehouseId, CancellationToken cancellationToken)
    {
        var armazem = await store.FindAsync(warehouseId, cancellationToken);

        if (armazem is null)
        {
            return WarehouseUsability.NotFound;
        }

        return armazem.Status is WarehouseStatus.Inactive ? WarehouseUsability.Inactive : WarehouseUsability.Usable;
    }
}

public sealed class RegisterReceipt(IInventoryItemStore store, IWarehouseStore warehouses, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        Guid warehouseId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound("Item não encontrado.");
        }

        switch (await WarehouseGuard.CheckAsync(warehouses, warehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return RegisterMovementResult.NotFound("Armazém não encontrado.");
            case WarehouseUsability.Inactive:
                return RegisterMovementResult.Conflict("Armazém inactivo.");
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterReceipt(warehouseId, quantity, reason, occurredOn, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return RegisterMovementResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return RegisterMovementResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.MovementReceipt,
                InventoryAuditEntityTypes.Movement,
                movimento.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","warehouseId":"{{warehouseId}}","quantity":{{movimento.Quantity}},"quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand, item.QuantityOnHandAt(warehouseId));
    }
}

public sealed class RegisterIssue(IInventoryItemStore store, IWarehouseStore warehouses, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        Guid warehouseId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound("Item não encontrado.");
        }

        switch (await WarehouseGuard.CheckAsync(warehouses, warehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return RegisterMovementResult.NotFound("Armazém não encontrado.");
            case WarehouseUsability.Inactive:
                return RegisterMovementResult.Conflict("Armazém inactivo.");
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterIssue(warehouseId, quantity, reason, occurredOn, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return RegisterMovementResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Sem quantidade suficiente nesse armazém, ou item inactivo:
            // conflito com o estado actual, não pedido malformado — 409, não 400.
            return RegisterMovementResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.MovementIssue,
                InventoryAuditEntityTypes.Movement,
                movimento.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","warehouseId":"{{warehouseId}}","quantity":{{movimento.Quantity}},"quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand, item.QuantityOnHandAt(warehouseId));
    }
}

/// <summary>Correcção de contagem. Exige motivo — ver <see cref="InventoryItem.RegisterAdjustment"/>.</summary>
public sealed class RegisterAdjustment(IInventoryItemStore store, IWarehouseStore warehouses, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        Guid warehouseId,
        decimal quantityDelta,
        string reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound("Item não encontrado.");
        }

        switch (await WarehouseGuard.CheckAsync(warehouses, warehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return RegisterMovementResult.NotFound("Armazém não encontrado.");
            case WarehouseUsability.Inactive:
                return RegisterMovementResult.Conflict("Armazém inactivo.");
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterAdjustment(warehouseId, quantityDelta, reason, occurredOn, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return RegisterMovementResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return RegisterMovementResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.MovementAdjustment,
                InventoryAuditEntityTypes.Movement,
                movimento.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","warehouseId":"{{warehouseId}}","quantity":{{movimento.Quantity}},"reason":"{{movimento.Reason}}","quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand, item.QuantityOnHandAt(warehouseId));
    }
}

/// <summary>
/// Transferência atómica entre dois armazéns do mesmo item — ver
/// <see cref="InventoryItem.Transfer"/>. Um único passo, sem estado
/// intermédio "em trânsito" (decisão confirmada 2026-08-31).
/// </summary>
public sealed class TransferStock(IInventoryItemStore store, IWarehouseStore warehouses, IAuditTrail audit, TimeProvider clock)
{
    public async Task<TransferResult> ExecuteAsync(
        Guid itemId,
        Guid fromWarehouseId,
        Guid toWarehouseId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return TransferResult.NotFound("Item não encontrado.");
        }

        switch (await WarehouseGuard.CheckAsync(warehouses, fromWarehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return TransferResult.NotFound("Armazém de origem não encontrado.");
            case WarehouseUsability.Inactive:
                return TransferResult.Conflict("Armazém de origem inactivo.");
        }

        switch (await WarehouseGuard.CheckAsync(warehouses, toWarehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return TransferResult.NotFound("Armazém de destino não encontrado.");
            case WarehouseUsability.Inactive:
                return TransferResult.Conflict("Armazém de destino inactivo.");
        }

        (StockMovement Out, StockMovement In) pernas;

        try
        {
            pernas = item.Transfer(fromWarehouseId, toWarehouseId, quantity, reason, occurredOn, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return TransferResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return TransferResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.MovementTransfer,
                InventoryAuditEntityTypes.Movement,
                pernas.Out.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","fromWarehouseId":"{{fromWarehouseId}}","toWarehouseId":"{{toWarehouseId}}","quantity":{{quantity}}}"""),
            cancellationToken);

        return TransferResult.Success(
            pernas.Out.Id, pernas.In.Id, item.QuantityOnHandAt(fromWarehouseId), item.QuantityOnHandAt(toWarehouseId));
    }
}

public sealed record RegisterMovementResult(
    RegisterMovementOutcome Outcome, Guid? MovementId, decimal? QuantityOnHand, decimal? QuantityAtWarehouse, string? Error)
{
    public static RegisterMovementResult Success(Guid movementId, decimal quantityOnHand, decimal quantityAtWarehouse) =>
        new(RegisterMovementOutcome.Registered, movementId, quantityOnHand, quantityAtWarehouse, null);

    public static RegisterMovementResult NotFound(string error) =>
        new(RegisterMovementOutcome.NotFound, null, null, null, error);

    public static RegisterMovementResult Rejected(string error) =>
        new(RegisterMovementOutcome.Rejected, null, null, null, error);

    public static RegisterMovementResult Conflict(string error) =>
        new(RegisterMovementOutcome.Conflict, null, null, null, error);
}

public enum RegisterMovementOutcome
{
    Registered,
    NotFound,

    /// <summary>Pedido malformado — quantidade não positiva, sem armazém, ou ajuste sem motivo. 400.</summary>
    Rejected,

    /// <summary>Item ou armazém inactivo, ou saída/ajuste que puxaria a quantidade para negativo. 409.</summary>
    Conflict,
}

public sealed record TransferResult(
    TransferOutcome Outcome,
    Guid? OutMovementId,
    Guid? InMovementId,
    decimal? QuantityAtSource,
    decimal? QuantityAtDestination,
    string? Error)
{
    public static TransferResult Success(
        Guid outMovementId, Guid inMovementId, decimal quantityAtSource, decimal quantityAtDestination) =>
        new(TransferOutcome.Registered, outMovementId, inMovementId, quantityAtSource, quantityAtDestination, null);

    public static TransferResult NotFound(string error) =>
        new(TransferOutcome.NotFound, null, null, null, null, error);

    public static TransferResult Rejected(string error) =>
        new(TransferOutcome.Rejected, null, null, null, null, error);

    public static TransferResult Conflict(string error) =>
        new(TransferOutcome.Conflict, null, null, null, null, error);
}

public enum TransferOutcome
{
    Registered,
    NotFound,

    /// <summary>Pedido malformado — quantidade não positiva, armazéns iguais, ou sem armazém. 400.</summary>
    Rejected,

    /// <summary>Armazém inactivo, ou sem quantidade suficiente na origem. 409.</summary>
    Conflict,
}
