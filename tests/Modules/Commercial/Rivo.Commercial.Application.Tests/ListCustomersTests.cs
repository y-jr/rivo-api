using Rivo.Commercial.Application.UseCases;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Commercial.Application.Tests;

/// <summary>
/// Paginação de clientes (ADR-068, item #10 do levantamento de pendências).
/// </summary>
public class ListCustomersTests
{
    [Fact]
    public async Task ListagemSemPagina_ContinuaADevolverTudo()
    {
        var store = new FakeCustomerStore();
        store.Registar("Padaria Central");
        store.Registar("Alfaiataria Nacional");
        store.Registar("Bazar Kianda");

        var (itens, total) = await new ListCustomers(store)
            .ExecuteAsync(includeInactive: false, pagina: null, CancellationToken.None);

        Assert.Equal(3, itens.Count);
        Assert.Null(total);
    }

    /// <summary>
    /// A fatia vem ordenada por nome — a mesma ordem que
    /// <c>CustomerStore.ListAsync</c> já usava antes da paginação, para que
    /// pedir a página 1 e a página 2 dê sempre o mesmo conjunto, sem
    /// sobreposição nem buraco.
    /// </summary>
    [Fact]
    public async Task ListagemComPagina_DevolveFatiaOrdenadaPorNomeEOTotal()
    {
        var store = new FakeCustomerStore();
        store.Registar("Padaria Central");
        store.Registar("Alfaiataria Nacional");
        store.Registar("Bazar Kianda");

        var lista = new ListCustomers(store);

        var (pagina1, total1) = await lista.ExecuteAsync(
            includeInactive: false, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total1);
        Assert.Equal(["Alfaiataria Nacional", "Bazar Kianda"], pagina1.Select(c => c.Name));

        var (pagina2, total2) = await lista.ExecuteAsync(
            includeInactive: false, new PageRequest(2, 2), CancellationToken.None);

        Assert.Equal(3, total2);
        Assert.Equal(["Padaria Central"], pagina2.Select(c => c.Name));
    }
}
