using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

public sealed class ListInventoryItems(IInventoryItemStore store)
{
    public async Task<IReadOnlyList<InventoryItem>> ExecuteAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        await store.ListAsync(includeInactive, cancellationToken);
}

public sealed class GetInventoryItem(IInventoryItemStore store)
{
    public Task<InventoryItem?> ExecuteAsync(Guid itemId, CancellationToken cancellationToken) =>
        store.FindAsync(itemId, cancellationToken);
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
}

public static class InventoryAuditEntityTypes
{
    public const string Item = "inventory.item";
}
