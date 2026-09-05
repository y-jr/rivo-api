using Rivo.Audit.Contracts;
using Rivo.Procurement.Application.UseCases;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// Emitir uma Ordem de Compra.
///
/// <para>
/// Duas invariantes, e nenhuma delas cabe no agregado:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <strong>Só de uma requisição aprovada nasce uma ordem.</strong> Depende do
/// estado de <em>outro</em> agregado.
/// </item>
/// <item>
/// <strong>A soma das ordens não excede o aprovado.</strong> Depende do
/// conjunto — três ordens de metade cada passariam uma a uma e, juntas,
/// encomendavam uma vez e meia o que foi decidido. É a mesma forma da guarda
/// de recepção, e o comentário no caso de uso di-lo: «o agregado não vê o
/// conjunto».
/// </item>
/// </list>
/// </summary>
public class IssuePurchaseOrderTests
{
    private static readonly TimeProvider Relogio =
        new RelogioFixo(new DateTimeOffset(2026, 1, 20, 10, 0, 0, TimeSpan.Zero));

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    private static IssuePurchaseOrder Caso(FakeProcurementStore store, FakeAuditTrail trilha) =>
        new(store, trilha, Relogio);

    [Fact]
    public async Task Emite_De_Requisicao_Aprovada_Dentro_Do_Valor()
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(100m, 5000m);   // 500 000 aprovados
        var fornecedor = store.Fornecedor();

        var resultado = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 100m, 5000m)], Actor(), CancellationToken.None);

        Assert.Equal(IssuePurchaseOrderOutcome.Issued, resultado.Outcome);
        Assert.Equal(500_000m, resultado.Total);
        Assert.Equal(1, store.Gravacoes);
    }

    [Theory]
    [InlineData(false)] // rascunho, nunca submetida
    public async Task Requisicao_Nao_Aprovada_Nao_Da_Ordem(bool aprovada)
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(100m, 5000m, aprovada);
        var fornecedor = store.Fornecedor();

        var resultado = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 10m, 5000m)], Actor(), CancellationToken.None);

        Assert.Equal(IssuePurchaseOrderOutcome.RequisitionNotApproved, resultado.Outcome);

        // A mensagem diz em que estado está, porque cada estado se corrige de
        // maneira diferente.
        Assert.Contains(RequisitionStatus.Draft.ToString(), resultado.Error);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// <strong>O caso central.</strong> Duas ordens que, isoladamente, cabem no
    /// aprovado — e que somadas o excedem.
    /// </summary>
    [Fact]
    public async Task Duas_Ordens_Que_Somadas_Excedem_E_Recusada_A_Segunda()
    {
        var store = new FakeProcurementStore();
        var trilha = new FakeAuditTrail();
        var requisicao = store.Requisitar(100m, 5000m);   // 500 000 aprovados
        var fornecedorA = store.Fornecedor();
        var fornecedorB = store.Fornecedor();

        // Dividir a compra por dois fornecedores é legítimo.
        var primeira = await Caso(store, trilha).ExecuteAsync(
            requisicao.Id, fornecedorA.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 60m, 5000m)], Actor(), CancellationToken.None);
        Assert.Equal(IssuePurchaseOrderOutcome.Issued, primeira.Outcome);

        // 250 000 cabiam, se ninguém contasse os 300 000 já encomendados.
        var segunda = await Caso(store, trilha).ExecuteAsync(
            requisicao.Id, fornecedorB.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 50m, 5000m)], Actor(), CancellationToken.None);

        Assert.Equal(IssuePurchaseOrderOutcome.ExceedsApproved, segunda.Outcome);
        Assert.Single(store.Ordens);
    }

    [Fact]
    public async Task Dividir_Por_Dois_Fornecedores_Dentro_Do_Aprovado_E_Aceite()
    {
        var store = new FakeProcurementStore();
        var trilha = new FakeAuditTrail();
        var requisicao = store.Requisitar(100m, 5000m);

        foreach (var _ in new[] { 1, 2 })
        {
            var resultado = await Caso(store, trilha).ExecuteAsync(
                requisicao.Id, store.Fornecedor().Id, null, null,
                [new NewPurchaseOrderLine("Cimento", 50m, 5000m)], Actor(), CancellationToken.None);

            Assert.Equal(IssuePurchaseOrderOutcome.Issued, resultado.Outcome);
        }

        // Exactamente o aprovado, em duas ordens. A fronteira é aceite.
        Assert.Equal(2, store.Ordens.Count);
        Assert.Equal(500_000m, store.Ordens.Sum(o => o.Total));
    }

    /// <summary>
    /// Desactivar um fornecedor tem de significar alguma coisa. Se ainda se lhe
    /// pudesse encomendar, a desactivação era um rótulo.
    /// </summary>
    [Fact]
    public async Task Fornecedor_Desactivado_Nao_Recebe_Encomendas()
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(100m, 5000m);
        var fornecedor = store.Fornecedor(activo: false);

        var resultado = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 10m, 5000m)], Actor(), CancellationToken.None);

        Assert.Equal(IssuePurchaseOrderOutcome.SupplierInactive, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Requisicao_Ou_Fornecedor_Inexistentes_Distinguem_Se()
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(100m, 5000m);

        var semRequisicao = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), null, null,
            [new NewPurchaseOrderLine("Cimento", 1m, 1m)], Actor(), CancellationToken.None);
        Assert.Equal(IssuePurchaseOrderOutcome.RequisitionNotFound, semRequisicao.Outcome);

        var semFornecedor = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, Guid.NewGuid(), null, null,
            [new NewPurchaseOrderLine("Cimento", 1m, 1m)], Actor(), CancellationToken.None);
        Assert.Equal(IssuePurchaseOrderOutcome.SupplierNotFound, semFornecedor.Outcome);
    }

    [Fact]
    public async Task Ordem_Sem_Linhas_Nao_Encomenda_Nada()
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(100m, 5000m);
        var fornecedor = store.Fornecedor();

        var resultado = await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null, [], Actor(), CancellationToken.None);

        Assert.Equal(IssuePurchaseOrderOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// A moeda é a da requisição, não uma escolha de quem encomenda: foi nela
    /// que o valor aprovado foi expresso, e comparar com outra exigiria um
    /// câmbio que ninguém decidiu.
    /// </summary>
    [Fact]
    public async Task A_Moeda_Vem_Da_Requisicao()
    {
        var store = new FakeProcurementStore();
        var requisicao = store.Requisitar(10m, 100m);
        var fornecedor = store.Fornecedor();

        await Caso(store, new FakeAuditTrail()).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 1m, 100m)], Actor(), CancellationToken.None);

        Assert.Equal(requisicao.Currency, Assert.Single(store.Ordens).Currency);
    }

    [Fact]
    public async Task Ordem_Auditada_Com_A_Requisicao_E_O_Total()
    {
        var store = new FakeProcurementStore();
        var trilha = new FakeAuditTrail();
        var requisicao = store.Requisitar(100m, 5000m);
        var fornecedor = store.Fornecedor();

        await Caso(store, trilha).ExecuteAsync(
            requisicao.Id, fornecedor.Id, null, null,
            [new NewPurchaseOrderLine("Cimento", 20m, 5000m)], Actor(), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(requisicao.Id.ToString(), registo.NewValue);
        Assert.Contains("100000", registo.NewValue);
    }
}
