using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Vista de leitura de um item, com os seus movimentos.
///
/// <para>
/// A entidade de domínio nunca sai desta camada (architecture/dependency-rules.md
/// §API) — mesma forma de <c>ProjectView</c> em `projects` e <c>VehicleView</c>
/// em `fleet`.
/// </para>
/// </summary>
public sealed record InventoryItemView(
    Guid ItemId,
    string Sku,
    string Name,
    string Unit,
    decimal QuantityOnHand,
    string Status,
    IReadOnlyList<StockMovementView> Movements);

public sealed record StockMovementView(
    Guid MovementId, string Type, decimal Quantity, string? Reason, DateOnly OccurredOn, DateTimeOffset RecordedAt);

internal static class InventoryItemViews
{
    internal static InventoryItemView ToView(InventoryItem item) => new(
        item.Id,
        item.Sku,
        item.Name,
        item.Unit,
        item.QuantityOnHand,
        item.Status.ToString(),
        [.. item.Movements.Select(m =>
            new StockMovementView(m.Id, m.Type.ToString(), m.Quantity, m.Reason, m.OccurredOn, m.RecordedAt))]);
}

public sealed class ListInventoryItems(IInventoryItemStore store)
{
    public async Task<IReadOnlyList<InventoryItemView>> ExecuteAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var itens = await store.ListAsync(includeInactive, cancellationToken);
        return [.. itens.Select(InventoryItemViews.ToView)];
    }
}

public sealed class GetInventoryItem(IInventoryItemStore store)
{
    public async Task<InventoryItemView?> ExecuteAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await store.FindAsync(itemId, cancellationToken);
        return item is null ? null : InventoryItemViews.ToView(item);
    }
}

public sealed class RegisterInventoryItem(IInventoryItemStore store, IAuditTrail audit)
{
    public async Task<RegisterItemResult> ExecuteAsync(
        string sku,
        string name,
        string unit,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        InventoryItem item;

        try
        {
            item = InventoryItem.Register(sku, name, unit);
        }
        catch (ArgumentException error)
        {
            return RegisterItemResult.Rejected(error.Message);
        }

        // Unicidade do SKU: o agregado não vê o conjunto, a verificação é
        // desta camada. Não substitui o índice único.
        if (await store.FindBySkuAsync(item.Sku, cancellationToken) is { } existente)
        {
            return RegisterItemResult.Duplicate(existente.Id);
        }

        await store.AddAsync(item, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.ItemRegistered,
                InventoryAuditEntityTypes.Item,
                item.Id.ToString(),
                context,
                NewValue: $$"""{"sku":"{{item.Sku}}","name":"{{item.Name}}"}"""),
            cancellationToken);

        return RegisterItemResult.Success(item.Id);
    }
}

public sealed class SetInventoryItemStatus(IInventoryItemStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid itemId, bool active, AuditContext context, CancellationToken cancellationToken)
    {
        var item = await store.FindForUpdateAsync(itemId, cancellationToken);

        if (item is null)
        {
            return false;
        }

        if (active)
        {
            item.Reactivate();
        }
        else
        {
            item.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active ? InventoryAuditActions.ItemReactivated : InventoryAuditActions.ItemDeactivated,
                InventoryAuditEntityTypes.Item,
                item.Id.ToString(),
                context),
            cancellationToken);

        return true;
    }
}

public sealed record RegisterItemResult(bool Succeeded, Guid? ItemId, string? Error)
{
    public static RegisterItemResult Success(Guid itemId) => new(true, itemId, null);

    public static RegisterItemResult Rejected(string error) => new(false, null, error);

    public static RegisterItemResult Duplicate(Guid existingId) => new(false, existingId, "SKU já existente.");
}

public static class InventoryAuditActions
{
    public const string ItemRegistered = "inventory.item.registered";
    public const string ItemDeactivated = "inventory.item.deactivated";
    public const string ItemReactivated = "inventory.item.reactivated";
    public const string MovementReceipt = "inventory.movement.receipt";
    public const string MovementIssue = "inventory.movement.issue";
    public const string MovementAdjustment = "inventory.movement.adjustment";
}

public static class InventoryAuditEntityTypes
{
    public const string Item = "inventory.item";
    public const string Movement = "inventory.movement";
}
