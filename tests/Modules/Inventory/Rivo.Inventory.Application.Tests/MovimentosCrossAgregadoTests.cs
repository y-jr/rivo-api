using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.UseCases;

namespace Rivo.Inventory.Application.Tests;

/// <summary>
/// A verificação de armazém nos movimentos de stock.
///
/// <para>
/// <strong>É cross-agregado, e é por isso que está aqui.</strong> O
/// <c>InventoryItem</c> sabe quanto tem em cada armazém e recusa sair mais do
/// que isso — isso é domínio e está coberto lá. O que ele <em>não</em> sabe é
/// se o armazém existe e se está activo: isso vive noutro agregado, noutro
/// armazenamento, e só o caso de uso os vê aos dois.
/// </para>
///
/// <para>
/// A transferência valida <strong>dois</strong> armazéns, e distingue a origem
/// do destino na mensagem — sem essa distinção, quem recebe o erro não sabe
/// qual dos dois corrigir.
/// </para>
/// </summary>
public class MovimentosCrossAgregadoTests
{
    private static readonly DateOnly Dia = new(2026, 3, 10);
    private static readonly TimeProvider Relogio =
        new RelogioFixo(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    [Fact]
    public async Task Saida_Para_Armazem_Inexistente_Da_NotFound()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");

        var resultado = await new RegisterIssue(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(item.Id, Guid.NewGuid(), 1m, "consumo", Dia, Actor(), CancellationToken.None);

        Assert.Equal(RegisterMovementOutcome.NotFound, resultado.Outcome);
        Assert.Equal(0, itens.Gravacoes);
    }

    /// <summary>
    /// Desactivar um armazém tem de significar alguma coisa. Se ainda se lhe
    /// pudesse movimentar stock, a desactivação era um rótulo.
    /// </summary>
    [Fact]
    public async Task Saida_De_Armazem_Inactivo_Da_Conflito()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");
        var armazem = armazens.Registar("AC-1", activo: false);

        var resultado = await new RegisterIssue(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(item.Id, armazem.Id, 1m, "consumo", Dia, Actor(), CancellationToken.None);

        // 409 e não 404: o armazém existe, o estado é que impede.
        Assert.Equal(RegisterMovementOutcome.Conflict, resultado.Outcome);
        Assert.Equal(0, itens.Gravacoes);
    }

    [Fact]
    public async Task Transferencia_Distingue_Origem_De_Destino_Inexistente()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");
        var real = armazens.Registar("AC-1");
        var caso = new TransferStock(itens, armazens, new FakeAuditTrail(), Relogio);

        var origemMa = await caso.ExecuteAsync(
            item.Id, Guid.NewGuid(), real.Id, 1m, null, Dia, Actor(), CancellationToken.None);
        Assert.Equal(TransferOutcome.NotFound, origemMa.Outcome);
        Assert.Contains("origem", origemMa.Error);

        var destinoMau = await caso.ExecuteAsync(
            item.Id, real.Id, Guid.NewGuid(), 1m, null, Dia, Actor(), CancellationToken.None);
        Assert.Equal(TransferOutcome.NotFound, destinoMau.Outcome);
        Assert.Contains("destino", destinoMau.Error);
    }

    [Fact]
    public async Task Transferencia_Recusa_Destino_Inactivo()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");
        var origem = armazens.Registar("AC-1");
        var destino = armazens.Registar("AC-2", activo: false);
        item.RegisterReceipt(origem.Id, 10m, 100m, null, Dia, Relogio.GetUtcNow());

        var resultado = await new TransferStock(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(item.Id, origem.Id, destino.Id, 5m, null, Dia, Actor(), CancellationToken.None);

        Assert.Equal(TransferOutcome.Conflict, resultado.Outcome);
        Assert.Contains("destino", resultado.Error);
    }

    /// <summary>
    /// A transferência move stock entre armazéns e <strong>não altera o
    /// total</strong>. É a invariante que dá sentido às duas pernas: se o total
    /// mudasse, a transferência seria uma entrada ou uma saída disfarçada.
    /// </summary>
    [Fact]
    public async Task Transferencia_Move_Entre_Armazens_Sem_Alterar_O_Total()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");
        var origem = armazens.Registar("AC-1");
        var destino = armazens.Registar("AC-2");
        item.RegisterReceipt(origem.Id, 10m, 100m, null, Dia, Relogio.GetUtcNow());
        var totalAntes = item.QuantityOnHand;

        var resultado = await new TransferStock(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(item.Id, origem.Id, destino.Id, 4m, null, Dia, Actor(), CancellationToken.None);

        Assert.Equal(TransferOutcome.Registered, resultado.Outcome);
        Assert.Equal(totalAntes, item.QuantityOnHand);
        Assert.Equal(6m, item.QuantityOnHandAt(origem.Id));
        Assert.Equal(4m, item.QuantityOnHandAt(destino.Id));
    }

    /// <summary>
    /// Sem stock suficiente na origem, a recusa vem do domínio — e o caso de
    /// uso traduz a excepção em conflito, não em pedido malformado. A
    /// distinção importa: 409 diz «o estado impede», 400 diria «o pedido está
    /// mal feito», e são coisas diferentes para quem chama.
    /// </summary>
    [Fact]
    public async Task Sem_Stock_Na_Origem_E_Conflito_E_Nao_Rejeicao()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var item = itens.Registar("SKU-1");
        var origem = armazens.Registar("AC-1");
        var destino = armazens.Registar("AC-2");

        var resultado = await new TransferStock(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(item.Id, origem.Id, destino.Id, 1m, null, Dia, Actor(), CancellationToken.None);

        Assert.Equal(TransferOutcome.Conflict, resultado.Outcome);
        Assert.Equal(0, itens.Gravacoes);
    }

    [Fact]
    public async Task Item_Inexistente_Da_NotFound_Antes_De_Olhar_Ao_Armazem()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();

        var resultado = await new RegisterIssue(itens, armazens, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), 1m, "consumo", Dia, Actor(), CancellationToken.None);

        Assert.Equal(RegisterMovementOutcome.NotFound, resultado.Outcome);
    }

    /// <summary>
    /// A trilha da saída guarda a quantidade em mão <em>depois</em> do
    /// movimento. É o que permite reconstruir a posição sem repetir a soma dos
    /// movimentos todos.
    /// </summary>
    [Fact]
    public async Task Saida_Auditada_Com_A_Quantidade_Resultante()
    {
        var itens = new FakeInventoryItemStore();
        var armazens = new FakeWarehouseStore();
        var trilha = new FakeAuditTrail();
        var item = itens.Registar("SKU-1");
        var armazem = armazens.Registar("AC-1");
        item.RegisterReceipt(armazem.Id, 10m, 100m, null, Dia, Relogio.GetUtcNow());

        await new RegisterIssue(itens, armazens, trilha, Relogio)
            .ExecuteAsync(item.Id, armazem.Id, 4m, "consumo", Dia, Actor(), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(item.Id.ToString(), registo.NewValue);
        Assert.Contains("\"quantityOnHand\":6", registo.NewValue);
    }
}
