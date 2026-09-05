using Rivo.Inventory.Application.UseCases;

namespace Rivo.Inventory.Application.Tests;

/// <summary>
/// Valorização de stock por período.
///
/// <para>
/// É uma projecção sobre movimentos acumulados, e as decisões que a tornam
/// certa ou errada são todas de janela: <strong>que movimentos entram, que
/// itens aparecem, e o que significa aparecer a zero</strong>. Nenhum agregado
/// conhece um intervalo de datas — o <c>InventoryItem</c> conhece os seus
/// movimentos, e mais nada.
/// </para>
///
/// <para>
/// ⚠ Isto responde «quanto valor entrou e saiu de stock neste período», e
/// <strong>não</strong> «quanto valia o stock a uma data». A distinção está
/// escrita no caso de uso e é fácil de perder ao ler o nome.
/// </para>
/// </summary>
public class StockValuationTests
{
    private static readonly DateTimeOffset Registo = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Inicio = new(2026, 2, 1);
    private static readonly DateOnly Fim = new(2026, 2, 28);

    [Fact]
    public async Task Janela_Invertida_E_Recusada()
    {
        var store = new FakeInventoryItemStore();

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Fim, Inicio, CancellationToken.None);

        Assert.Equal(StockValuationOutcome.Rejected, resultado.Outcome);
        Assert.Null(resultado.Entries);
    }

    /// <summary>
    /// As fronteiras são inclusivas nos dois extremos. Um movimento no
    /// primeiro ou no último dia do período conta — e é precisamente aí que um
    /// <c>&gt;</c> em vez de <c>&gt;=</c> passa despercebido.
    /// </summary>
    [Fact]
    public async Task As_Fronteiras_Da_Janela_Sao_Inclusivas()
    {
        var store = new FakeInventoryItemStore();
        var item = store.Registar("SKU-1");
        var armazem = Guid.NewGuid();

        item.RegisterReceipt(armazem, 10m, 100m, null, Inicio, Registo);
        item.RegisterReceipt(armazem, 10m, 100m, null, Fim, Registo);

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        var entrada = Assert.Single(resultado.Entries!);
        Assert.Equal(2000m, entrada.PeriodValue);
    }

    [Fact]
    public async Task Movimentos_Fora_Da_Janela_Nao_Contam()
    {
        var store = new FakeInventoryItemStore();
        var item = store.Registar("SKU-1");
        var armazem = Guid.NewGuid();

        item.RegisterReceipt(armazem, 10m, 100m, null, Inicio.AddDays(-1), Registo);
        item.RegisterReceipt(armazem, 5m, 100m, null, Inicio.AddDays(3), Registo);
        item.RegisterReceipt(armazem, 10m, 100m, null, Fim.AddDays(1), Registo);

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        var entrada = Assert.Single(resultado.Entries!);
        Assert.Equal(500m, entrada.PeriodValue);
    }

    /// <summary>
    /// Um item sem movimento nenhum na janela <strong>não aparece</strong> —
    /// não tem nada a dizer sobre o período, e listá-lo a zero seria ruído.
    /// </summary>
    [Fact]
    public async Task Item_Sem_Movimento_Na_Janela_Nao_Aparece()
    {
        var store = new FakeInventoryItemStore();
        var comMovimento = store.Registar("SKU-1");
        store.Registar("SKU-2"); // nunca se mexeu
        comMovimento.RegisterReceipt(Guid.NewGuid(), 1m, 100m, null, Inicio, Registo);

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        Assert.Equal("SKU-1", Assert.Single(resultado.Entries!).Sku);
    }

    /// <summary>
    /// <strong>A distinção que mais importa.</strong> Um item que recebeu e
    /// saiu o mesmo valor na janela aparece <em>com zero</em> — e isso não é o
    /// mesmo que não aparecer.
    ///
    /// <para>
    /// «Não houve actividade» e «a actividade anulou-se» são factos diferentes,
    /// e quem lê um relatório de valorização precisa de os distinguir. O filtro
    /// é por <em>haver movimento</em>, não por a soma ser diferente de zero.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Item_Com_Movimento_Que_Soma_Zero_Aparece_A_Zero()
    {
        var store = new FakeInventoryItemStore();
        var item = store.Registar("SKU-1");
        var armazem = Guid.NewGuid();

        item.RegisterReceipt(armazem, 10m, 100m, null, Inicio, Registo);
        item.RegisterIssue(armazem, 10m, "consumo", Inicio.AddDays(1), Registo);

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        var entrada = Assert.Single(resultado.Entries!);
        Assert.Equal(0m, entrada.PeriodValue);
    }

    [Fact]
    public async Task As_Entradas_Vem_Ordenadas_Por_Sku()
    {
        var store = new FakeInventoryItemStore();
        foreach (var sku in new[] { "SKU-C", "SKU-A", "SKU-B" })
        {
            store.Registar(sku).RegisterReceipt(Guid.NewGuid(), 1m, 10m, null, Inicio, Registo);
        }

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        Assert.Equal(["SKU-A", "SKU-B", "SKU-C"], resultado.Entries!.Select(e => e.Sku));
    }

    /// <summary>
    /// Itens inactivos entram na valorização. Desactivar um item impede
    /// movimentos novos, mas os que já houve continuam a ter valor no período
    /// em que aconteceram — excluí-los faria o total do relatório deixar de
    /// bater certo com o que se movimentou.
    /// </summary>
    [Fact]
    public async Task Item_Inactivo_Com_Movimento_Na_Janela_Continua_A_Contar()
    {
        var store = new FakeInventoryItemStore();
        var item = store.Registar("SKU-1");
        item.RegisterReceipt(Guid.NewGuid(), 10m, 100m, null, Inicio, Registo);
        item.Deactivate();

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Fim, CancellationToken.None);

        Assert.Equal(1000m, Assert.Single(resultado.Entries!).PeriodValue);
    }

    [Fact]
    public async Task Janela_De_Um_Dia_So_E_Valida()
    {
        var store = new FakeInventoryItemStore();
        var item = store.Registar("SKU-1");
        item.RegisterReceipt(Guid.NewGuid(), 3m, 100m, null, Inicio, Registo);

        var resultado = await new GetStockValuationByPeriod(store)
            .ExecuteAsync(Inicio, Inicio, CancellationToken.None);

        Assert.Equal(StockValuationOutcome.Computed, resultado.Outcome);
        Assert.Equal(300m, Assert.Single(resultado.Entries!).PeriodValue);
    }
}
