using Rivo.Fleet.Application.UseCases;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Fleet.Application.Tests;

/// <summary>Paginação real (ADR-068, item #10) nas listagens de `fleet`.</summary>
public class PaginationTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider Relogio = new RelogioFixo(Agora);

    [Fact]
    public async Task ListVehicles_SemPagina_DevolveTudo()
    {
        var store = new FakeVehicleStore();
        store.Registar("LD-01-AA");
        store.Registar("LD-02-BB");
        store.Registar("LD-03-CC");

        var (itens, total) = await new ListVehicles(store, Relogio).ExecuteAsync(
            includeInactive: false, pagina: null, CancellationToken.None);

        Assert.Equal(3, itens.Count);
        Assert.Null(total);
    }

    [Fact]
    public async Task ListVehicles_ComPagina_DevolveFatiaOrdenadaPorMatriculaEOTotal()
    {
        var store = new FakeVehicleStore();
        store.Registar("LD-03-CC");
        store.Registar("LD-01-AA");
        store.Registar("LD-02-BB");

        var (pagina1, total) = await new ListVehicles(store, Relogio).ExecuteAsync(
            includeInactive: false, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(["LD-01-AA", "LD-02-BB"], pagina1.Select(v => v.PlateNumber));

        var (ultima, total2) = await new ListVehicles(store, Relogio).ExecuteAsync(
            includeInactive: false, new PageRequest(2, 2), CancellationToken.None);

        Assert.Equal(3, total2);
        Assert.Equal(["LD-03-CC"], ultima.Select(v => v.PlateNumber));
    }
}
