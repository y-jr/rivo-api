using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Contracts;

namespace Rivo.Inventory.Application;

/// <summary>
/// O contrato publicado de valorização de stock para composição (Analytics
/// &amp; IA, módulo 10) — primeiro contrato publicado de `inventory`.
/// </summary>
public sealed class InventoryValuationOverview(IInventoryItemStore store) : IInventoryValuationOverview
{
    public Task<decimal> GetCurrentStockValueAsync(CancellationToken cancellationToken) =>
        store.SumCurrentStockValueAsync(cancellationToken);

    public Task<decimal> GetPeriodValuationAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        store.SumMovementValueInPeriodAsync(from, to, cancellationToken);
}
