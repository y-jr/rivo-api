using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Contracts;

namespace Rivo.Inventory.Application;

/// <summary>
/// O catálogo publicado de `inventory`. Mesmo desenho de
/// <c>CustomerDirectory</c> e <c>SupplierDirectory</c>: traduz o agregado para
/// o contrato, sem deixar sair o modelo interno (ADR-010).
/// </summary>
public sealed class InventoryCatalogue(IInventoryItemStore store) : IInventoryCatalogue
{
    public async Task<IReadOnlyList<CatalogueItem>> ListAllAsync(CancellationToken cancellationToken)
    {
        // `includeInactive: true` — ver `IInventoryCatalogue.ListAllAsync`.
        var artigos = await store.ListAsync(includeInactive: true, cancellationToken);

        // Nem `QuantityOnHand` nem `AverageCost` saem por aqui, e é
        // deliberado: o catálogo diz que artigos existem, não quantos há nem
        // quanto valem. Quem precisa disso tem
        // `IInventoryValuationOverview` — e o SAF-T, que é o primeiro
        // consumidor, não pergunta nenhuma das duas coisas em `Product`.
        return [.. artigos.Select(a => new CatalogueItem(a.Id, a.Sku, a.Name, a.Unit))];
    }
}
