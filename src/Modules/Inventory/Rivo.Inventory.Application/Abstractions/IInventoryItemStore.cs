using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.Abstractions;

/// <summary>
/// Persistência de `inventory`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IInventoryItemStore
{
    Task<InventoryItem?> FindAsync(Guid itemId, CancellationToken cancellationToken);

    Task<InventoryItem?> FindForUpdateAsync(Guid itemId, CancellationToken cancellationToken);

    Task<InventoryItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItem>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(InventoryItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Valor do stock agora: soma de <c>QuantityOnHand × AverageCost</c>
    /// sobre os itens activos — estado corrente, não reconstrução de uma
    /// data passada (mesma fronteira que <see cref="SumMovementValueInPeriodAsync"/>
    /// já respeita, e que `GetOutstandingReceivablesAsync` também traça do
    /// lado de `finance`). Primeiro consumidor: Analytics & IA (módulo 10).
    /// </summary>
    Task<decimal> SumCurrentStockValueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Soma de <c>Quantity × UnitCost</c> (com sinal — negativo em Saída)
    /// sobre os movimentos ocorridos no período, todos os itens — a mesma
    /// conta de <c>GetStockValuationByPeriod</c>, agregada num só total em
    /// vez de por item.
    /// </summary>
    Task<decimal> SumMovementValueInPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
