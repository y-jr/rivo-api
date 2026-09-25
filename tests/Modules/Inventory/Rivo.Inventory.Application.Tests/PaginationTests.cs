using Rivo.Inventory.Application.UseCases;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Inventory.Application.Tests;

/// <summary>Paginação real (ADR-068, item #10) nas listagens de `inventory`.</summary>
public class PaginationTests
{
    [Fact]
    public async Task ListInventoryItems_SemPagina_DevolveTudo()
    {
        var store = new FakeInventoryItemStore();
        store.Registar("SKU-001");
        store.Registar("SKU-002");
        store.Registar("SKU-003");

        var (itens, total) = await new ListInventoryItems(store).ExecuteAsync(
            includeInactive: false, pagina: null, CancellationToken.None);

        Assert.Equal(3, itens.Count);
        Assert.Null(total);
    }

    [Fact]
    public async Task ListInventoryItems_ComPagina_DevolveFatiaOrdenadaPorSkuEOTotal()
    {
        var store = new FakeInventoryItemStore();
        store.Registar("SKU-003");
        store.Registar("SKU-001");
        store.Registar("SKU-002");

        var (pagina1, total) = await new ListInventoryItems(store).ExecuteAsync(
            includeInactive: false, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(["SKU-001", "SKU-002"], pagina1.Select(i => i.Sku));

        var (ultima, total2) = await new ListInventoryItems(store).ExecuteAsync(
            includeInactive: false, new PageRequest(2, 2), CancellationToken.None);

        Assert.Equal(3, total2);
        Assert.Equal(["SKU-003"], ultima.Select(i => i.Sku));
    }
}
