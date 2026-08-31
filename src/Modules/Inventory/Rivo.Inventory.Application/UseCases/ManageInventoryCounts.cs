using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Vista de leitura de uma contagem, com as suas linhas. Mesma disciplina de
/// <c>InventoryItemView</c> — a entidade de domínio nunca sai desta camada.
/// </summary>
public sealed record InventoryCountView(
    Guid CountId,
    Guid WarehouseId,
    DateOnly OccurredOn,
    string Status,
    string? CancellationReason,
    IReadOnlyList<InventoryCountLineView> Lines);

public sealed record InventoryCountLineView(
    Guid LineId, Guid ItemId, decimal ExpectedQuantity, decimal CountedQuantity, decimal Variance);

internal static class InventoryCountViews
{
    internal static InventoryCountView ToView(InventoryCount count) => new(
        count.Id,
        count.WarehouseId,
        count.OccurredOn,
        count.Status.ToString(),
        count.CancellationReason,
        [.. count.Lines.Select(l => new InventoryCountLineView(l.Id, l.ItemId, l.ExpectedQuantity, l.CountedQuantity, l.Variance))]);
}

public sealed class ListInventoryCounts(IInventoryCountStore store)
{
    public async Task<IReadOnlyList<InventoryCountView>> ExecuteAsync(Guid? warehouseId, CancellationToken cancellationToken)
    {
        var contagens = await store.ListAsync(warehouseId, cancellationToken);
        return [.. contagens.Select(InventoryCountViews.ToView)];
    }
}

public sealed class GetInventoryCount(IInventoryCountStore store)
{
    public async Task<InventoryCountView?> ExecuteAsync(Guid countId, CancellationToken cancellationToken)
    {
        var contagem = await store.FindAsync(countId, cancellationToken);
        return contagem is null ? null : InventoryCountViews.ToView(contagem);
    }
}

public sealed class OpenInventoryCount(IInventoryCountStore store, IWarehouseStore warehouses, IAuditTrail audit)
{
    public async Task<OpenCountResult> ExecuteAsync(
        Guid warehouseId, DateOnly occurredOn, AuditContext context, CancellationToken cancellationToken)
    {
        switch (await WarehouseGuard.CheckAsync(warehouses, warehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return OpenCountResult.NotFound("Armazém não encontrado.");
            case WarehouseUsability.Inactive:
                return OpenCountResult.Conflict("Armazém inactivo.");
        }

        InventoryCount count;

        try
        {
            count = InventoryCount.Open(warehouseId, occurredOn);
        }
        catch (ArgumentException error)
        {
            return OpenCountResult.Rejected(error.Message);
        }

        await store.AddAsync(count, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountOpened,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"warehouseId":"{{warehouseId}}","occurredOn":"{{occurredOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return OpenCountResult.Success(count.Id);
    }
}

public sealed class AddInventoryCountLine(IInventoryCountStore counts, IInventoryItemStore items, IAuditTrail audit)
{
    public async Task<AddCountLineResult> ExecuteAsync(
        Guid countId, Guid itemId, decimal countedQuantity, AuditContext context, CancellationToken cancellationToken)
    {
        var count = await counts.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return AddCountLineResult.NotFound("Contagem não encontrada.");
        }

        var item = await items.FindAsync(itemId, cancellationToken);

        if (item is null)
        {
            return AddCountLineResult.NotFound("Item não encontrado.");
        }

        var expectedQuantity = item.QuantityOnHandAt(count.WarehouseId);

        InventoryCountLine linha;

        try
        {
            linha = count.AddLine(itemId, countedQuantity, expectedQuantity);
        }
        catch (ArgumentException error)
        {
            return AddCountLineResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return AddCountLineResult.Conflict(error.Message);
        }

        await counts.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountLineAdded,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","expectedQuantity":{{linha.ExpectedQuantity}},"countedQuantity":{{linha.CountedQuantity}}}"""),
            cancellationToken);

        return AddCountLineResult.Success(linha.Id, linha.ExpectedQuantity, linha.CountedQuantity, linha.Variance);
    }
}

/// <summary>
/// Fecha a contagem e gera, na mesma transacção, um Ajuste
/// (<see cref="InventoryItem.RegisterAdjustment"/>) por cada linha com
/// variância — tudo ou nada: se um item recusar o ajuste (por exemplo,
/// ficou inactivo entretanto), nada fica gravado, nem sequer o fecho da
/// contagem. Mesma disciplina de "Emitir passa a lançar, na mesma
/// transacção" já usada em `finance`.
/// </summary>
public sealed class CloseInventoryCount(IInventoryCountStore counts, IInventoryItemStore items, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CloseCountResult> ExecuteAsync(Guid countId, AuditContext context, CancellationToken cancellationToken)
    {
        var count = await counts.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return CloseCountResult.NotFound("Contagem não encontrada.");
        }

        try
        {
            count.Close();
        }
        catch (InvalidOperationException error)
        {
            return CloseCountResult.Conflict(error.Message);
        }

        var geradas = new List<(Guid MovementId, Guid ItemId, decimal Variance)>();
        var agora = clock.GetUtcNow();

        foreach (var linha in count.Lines.Where(l => l.Variance != 0))
        {
            var item = await items.FindForUpdateAsync(linha.ItemId, cancellationToken);

            if (item is null)
            {
                return CloseCountResult.Conflict($"Item {linha.ItemId} da contagem já não existe.");
            }

            StockMovement movimento;

            try
            {
                movimento = item.RegisterAdjustment(
                    count.WarehouseId, linha.Variance, $"Contagem {count.Id}", count.OccurredOn, agora);
            }
            catch (InvalidOperationException error)
            {
                return CloseCountResult.Conflict($"Item {linha.ItemId}: {error.Message}");
            }

            geradas.Add((movimento.Id, linha.ItemId, linha.Variance));
        }

        await counts.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountClosed,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"warehouseId":"{{count.WarehouseId}}","linesWithVariance":{{geradas.Count}}}"""),
            cancellationToken);

        foreach (var (movementId, itemId, variance) in geradas)
        {
            await audit.RecordAsync(
                new AuditRecord(
                    InventoryAuditActions.MovementAdjustment,
                    InventoryAuditEntityTypes.Movement,
                    movementId.ToString(),
                    context,
                    NewValue: $$"""{"itemId":"{{itemId}}","warehouseId":"{{count.WarehouseId}}","quantity":{{variance}},"countId":"{{count.Id}}"}"""),
                cancellationToken);
        }

        return CloseCountResult.Success([.. geradas.Select(g => g.MovementId)]);
    }
}

public sealed class CancelInventoryCount(IInventoryCountStore store, IAuditTrail audit)
{
    public async Task<CancelCountResult> ExecuteAsync(
        Guid countId, string reason, AuditContext context, CancellationToken cancellationToken)
    {
        var count = await store.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return CancelCountResult.NotFound("Contagem não encontrada.");
        }

        try
        {
            count.Cancel(reason);
        }
        catch (ArgumentException error)
        {
            return CancelCountResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return CancelCountResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountCancelled,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{count.CancellationReason}}"}"""),
            cancellationToken);

        return CancelCountResult.Success();
    }
}

public sealed record OpenCountResult(OpenCountOutcome Outcome, Guid? CountId, string? Error)
{
    public static OpenCountResult Success(Guid countId) => new(OpenCountOutcome.Opened, countId, null);

    public static OpenCountResult NotFound(string error) => new(OpenCountOutcome.NotFound, null, error);

    public static OpenCountResult Rejected(string error) => new(OpenCountOutcome.Rejected, null, error);

    public static OpenCountResult Conflict(string error) => new(OpenCountOutcome.Conflict, null, error);
}

public enum OpenCountOutcome
{
    Opened,
    NotFound,

    /// <summary>Pedido malformado — sem armazém. 400.</summary>
    Rejected,

    /// <summary>Armazém inactivo. 409.</summary>
    Conflict,
}

public sealed record AddCountLineResult(
    AddCountLineOutcome Outcome, Guid? LineId, decimal? ExpectedQuantity, decimal? CountedQuantity, decimal? Variance, string? Error)
{
    public static AddCountLineResult Success(Guid lineId, decimal expectedQuantity, decimal countedQuantity, decimal variance) =>
        new(AddCountLineOutcome.Added, lineId, expectedQuantity, countedQuantity, variance, null);

    public static AddCountLineResult NotFound(string error) => new(AddCountLineOutcome.NotFound, null, null, null, null, error);

    public static AddCountLineResult Rejected(string error) => new(AddCountLineOutcome.Rejected, null, null, null, null, error);

    public static AddCountLineResult Conflict(string error) => new(AddCountLineOutcome.Conflict, null, null, null, null, error);
}

public enum AddCountLineOutcome
{
    Added,
    NotFound,

    /// <summary>Pedido malformado — sem item, ou quantidade negativa. 400.</summary>
    Rejected,

    /// <summary>Contagem já não está aberta, ou item já tem linha nesta contagem. 409.</summary>
    Conflict,
}

public sealed record CloseCountResult(CloseCountOutcome Outcome, IReadOnlyList<Guid>? GeneratedAdjustmentIds, string? Error)
{
    public static CloseCountResult Success(IReadOnlyList<Guid> generatedAdjustmentIds) =>
        new(CloseCountOutcome.Closed, generatedAdjustmentIds, null);

    public static CloseCountResult NotFound(string error) => new(CloseCountOutcome.NotFound, null, error);

    public static CloseCountResult Conflict(string error) => new(CloseCountOutcome.Conflict, null, error);
}

public enum CloseCountOutcome
{
    Closed,
    NotFound,

    /// <summary>Contagem já não está aberta, sem nenhuma linha, ou um item recusou o ajuste gerado. 409.</summary>
    Conflict,
}

public sealed record CancelCountResult(CancelCountOutcome Outcome, string? Error)
{
    public static CancelCountResult Success() => new(CancelCountOutcome.Cancelled, null);

    public static CancelCountResult NotFound(string error) => new(CancelCountOutcome.NotFound, error);

    public static CancelCountResult Rejected(string error) => new(CancelCountOutcome.Rejected, error);

    public static CancelCountResult Conflict(string error) => new(CancelCountOutcome.Conflict, error);
}

public enum CancelCountOutcome
{
    Cancelled,
    NotFound,

    /// <summary>Pedido malformado — sem motivo. 400.</summary>
    Rejected,

    /// <summary>Contagem já não está aberta. 409.</summary>
    Conflict,
}
