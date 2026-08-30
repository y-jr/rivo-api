using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

public sealed class RegisterReceipt(IInventoryItemStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound();
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterReceipt(quantity, reason, occurredOn, clock.GetUtcNow());
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
                NewValue: $$"""{"itemId":"{{itemId}}","quantity":{{movimento.Quantity}},"quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand);
    }
}

public sealed class RegisterIssue(IInventoryItemStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound();
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterIssue(quantity, reason, occurredOn, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return RegisterMovementResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Sem quantidade suficiente, ou item inactivo: conflito com o
            // estado actual, não pedido malformado — 409, não 400.
            return RegisterMovementResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.MovementIssue,
                InventoryAuditEntityTypes.Movement,
                movimento.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","quantity":{{movimento.Quantity}},"quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand);
    }
}

/// <summary>Correcção de contagem. Exige motivo — ver <see cref="InventoryItem.RegisterAdjustment"/>.</summary>
public sealed class RegisterAdjustment(IInventoryItemStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RegisterMovementResult> ExecuteAsync(
        Guid itemId,
        decimal quantityDelta,
        string reason,
        DateOnly occurredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return RegisterMovementResult.NotFound();
        }

        StockMovement movimento;

        try
        {
            movimento = item.RegisterAdjustment(quantityDelta, reason, occurredOn, clock.GetUtcNow());
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
                NewValue: $$"""{"itemId":"{{itemId}}","quantity":{{movimento.Quantity}},"reason":"{{movimento.Reason}}","quantityOnHand":{{item.QuantityOnHand}}}"""),
            cancellationToken);

        return RegisterMovementResult.Success(movimento.Id, item.QuantityOnHand);
    }
}

public sealed record RegisterMovementResult(
    RegisterMovementOutcome Outcome, Guid? MovementId, decimal? QuantityOnHand, string? Error)
{
    public static RegisterMovementResult Success(Guid movementId, decimal quantityOnHand) =>
        new(RegisterMovementOutcome.Registered, movementId, quantityOnHand, null);

    public static RegisterMovementResult NotFound() =>
        new(RegisterMovementOutcome.NotFound, null, null, "Item não encontrado.");

    public static RegisterMovementResult Rejected(string error) =>
        new(RegisterMovementOutcome.Rejected, null, null, error);

    public static RegisterMovementResult Conflict(string error) =>
        new(RegisterMovementOutcome.Conflict, null, null, error);
}

public enum RegisterMovementOutcome
{
    Registered,
    NotFound,

    /// <summary>Pedido malformado — quantidade não positiva, ou ajuste sem motivo. 400.</summary>
    Rejected,

    /// <summary>Item inactivo, ou saída/ajuste que puxaria a quantidade para negativo. 409.</summary>
    Conflict,
}
