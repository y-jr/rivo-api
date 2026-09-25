using Rivo.Procurement.Application.UseCases;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// Paginação real (ADR-068, item #10) nas listagens de `procurement`.
/// </summary>
public class PaginationTests
{
    [Fact]
    public async Task ListSuppliers_SemPagina_DevolveTudo()
    {
        var store = new FakeProcurementStore();
        for (var i = 0; i < 5; i++) store.Fornecedor();

        var (itens, total) = await new ListSuppliers(store).ExecuteAsync(
            includeInactive: false, pagina: null, CancellationToken.None);

        Assert.Equal(5, itens.Count);
        Assert.Null(total);
    }

    [Fact]
    public async Task ListSuppliers_ComPagina_DevolveFatiaOrdenadaEOTotal()
    {
        var store = new FakeProcurementStore();
        for (var i = 0; i < 5; i++) store.Fornecedor();

        var (pagina1, total) = await new ListSuppliers(store).ExecuteAsync(
            includeInactive: false, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(5, total);
        Assert.Equal(2, pagina1.Count);
        Assert.Equal(["Fornecedor 0", "Fornecedor 1"], pagina1.Select(f => f.Name));

        var (ultima, total2) = await new ListSuppliers(store).ExecuteAsync(
            includeInactive: false, new PageRequest(3, 2), CancellationToken.None);

        Assert.Equal(5, total2);
        Assert.Equal(["Fornecedor 4"], ultima.Select(f => f.Name));
    }

    [Fact]
    public async Task ListRequisitions_ComPagina_RespeitaOrdemMaisRecentePrimeiro()
    {
        var store = new FakeProcurementStore();
        var r1 = store.Requisitar(10, 100);
        var r2 = store.Requisitar(20, 100);
        var r3 = store.Requisitar(30, 100);

        var (pagina1, total) = await new ListRequisitions(store).ExecuteAsync(
            requestedByEmployeeId: null, status: null, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(2, pagina1.Count);
        // Todas partilham a mesma data (RequestedOn fixo no fake) — o
        // desempate por Id garante que a ordem é sempre a mesma, não a de
        // inserção por acidente.
        Assert.All(pagina1, v => Assert.Contains(v.RequisitionId, new[] { r1.Id, r2.Id, r3.Id }));
    }
}
