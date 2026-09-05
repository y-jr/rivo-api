using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Application.Tests;

/// <summary>
/// Itens em memória.
///
/// <para>
/// Escrita por inteiro: <c>IInventoryItemStore</c> tem oito membros. A base
/// parcial de `hr` e `procurement` existe para interfaces de quarenta — aqui
/// seria cerimónia.
/// </para>
/// </summary>
internal sealed class FakeInventoryItemStore : IInventoryItemStore
{
    private readonly List<InventoryItem> _itens = [];

    public int Gravacoes { get; private set; }

    public InventoryItem Registar(string sku, string nome = "Artigo", bool activo = true)
    {
        var item = InventoryItem.Register(sku, nome, "un");
        if (!activo) item.Deactivate();
        _itens.Add(item);
        return item;
    }

    public Task<InventoryItem?> FindAsync(Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(_itens.SingleOrDefault(i => i.Id == itemId));

    public Task<InventoryItem?> FindForUpdateAsync(Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(_itens.SingleOrDefault(i => i.Id == itemId));

    public Task<InventoryItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken) =>
        Task.FromResult(_itens.SingleOrDefault(i => i.Sku == sku));

    public Task<IReadOnlyList<InventoryItem>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InventoryItem>>(
            [.. _itens.Where(i => includeInactive || i.Status == InventoryItemStatus.Active)]);

    public Task AddAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        _itens.Add(item);
        return Task.CompletedTask;
    }

    public Task<decimal> SumCurrentStockValueAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a SumCurrentStockValueAsync.");

    public Task<decimal> SumMovementValueInPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a SumMovementValueInPeriodAsync.");

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Armazéns em memória. É o que o <c>WarehouseGuard</c> consulta — o agregado
/// do item não sabe se o armazém existe nem se está activo.
/// </summary>
internal sealed class FakeWarehouseStore : IWarehouseStore
{
    private readonly List<Warehouse> _armazens = [];

    public Warehouse Registar(string codigo, bool activo = true)
    {
        var armazem = Warehouse.Register(codigo, $"Armazém {codigo}");
        if (!activo) armazem.Deactivate();
        _armazens.Add(armazem);
        return armazem;
    }

    public Task<Warehouse?> FindAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(_armazens.SingleOrDefault(a => a.Id == warehouseId));

    public Task<Warehouse?> FindForUpdateAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        Task.FromResult(_armazens.SingleOrDefault(a => a.Id == warehouseId));

    public Task<Warehouse?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_armazens.SingleOrDefault(a => a.Code == code));

    public Task<IReadOnlyList<Warehouse>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Warehouse>>([.. _armazens]);

    public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken)
    {
        _armazens.Add(warehouse);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
