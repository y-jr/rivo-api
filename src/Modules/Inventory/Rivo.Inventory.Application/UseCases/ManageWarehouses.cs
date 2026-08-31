using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Vista de leitura de um armazém. Mesma disciplina de <c>InventoryItemView</c>
/// — a entidade de domínio nunca sai desta camada.
/// </summary>
public sealed record WarehouseView(Guid WarehouseId, string Code, string Name, string Status);

internal static class WarehouseViews
{
    internal static WarehouseView ToView(Warehouse warehouse) =>
        new(warehouse.Id, warehouse.Code, warehouse.Name, warehouse.Status.ToString());
}

public sealed class ListWarehouses(IWarehouseStore store)
{
    public async Task<IReadOnlyList<WarehouseView>> ExecuteAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var armazens = await store.ListAsync(includeInactive, cancellationToken);
        return [.. armazens.Select(WarehouseViews.ToView)];
    }
}

public sealed class GetWarehouse(IWarehouseStore store)
{
    public async Task<WarehouseView?> ExecuteAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var armazem = await store.FindAsync(warehouseId, cancellationToken);
        return armazem is null ? null : WarehouseViews.ToView(armazem);
    }
}

public sealed class RegisterWarehouse(IWarehouseStore store, IAuditTrail audit)
{
    public async Task<RegisterWarehouseResult> ExecuteAsync(
        string code, string name, AuditContext context, CancellationToken cancellationToken)
    {
        Warehouse warehouse;

        try
        {
            warehouse = Warehouse.Register(code, name);
        }
        catch (ArgumentException error)
        {
            return RegisterWarehouseResult.Rejected(error.Message);
        }

        // Unicidade do código: o agregado não vê o conjunto, a verificação é
        // desta camada. Não substitui o índice único.
        if (await store.FindByCodeAsync(warehouse.Code, cancellationToken) is { } existente)
        {
            return RegisterWarehouseResult.Duplicate(existente.Id);
        }

        await store.AddAsync(warehouse, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.WarehouseRegistered,
                InventoryAuditEntityTypes.Warehouse,
                warehouse.Id.ToString(),
                context,
                NewValue: $$"""{"code":"{{warehouse.Code}}","name":"{{warehouse.Name}}"}"""),
            cancellationToken);

        return RegisterWarehouseResult.Success(warehouse.Id);
    }
}

public sealed class SetWarehouseStatus(IWarehouseStore store, IAuditTrail audit)
{
    public async Task<bool> ExecuteAsync(
        Guid warehouseId, bool active, AuditContext context, CancellationToken cancellationToken)
    {
        var warehouse = await store.FindForUpdateAsync(warehouseId, cancellationToken);

        if (warehouse is null)
        {
            return false;
        }

        if (active)
        {
            warehouse.Reactivate();
        }
        else
        {
            warehouse.Deactivate();
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active ? InventoryAuditActions.WarehouseReactivated : InventoryAuditActions.WarehouseDeactivated,
                InventoryAuditEntityTypes.Warehouse,
                warehouse.Id.ToString(),
                context),
            cancellationToken);

        return true;
    }
}

public sealed record RegisterWarehouseResult(bool Succeeded, Guid? WarehouseId, string? Error)
{
    public static RegisterWarehouseResult Success(Guid warehouseId) => new(true, warehouseId, null);

    public static RegisterWarehouseResult Rejected(string error) => new(false, null, error);

    public static RegisterWarehouseResult Duplicate(Guid existingId) =>
        new(false, existingId, "Código de armazém já existente.");
}
