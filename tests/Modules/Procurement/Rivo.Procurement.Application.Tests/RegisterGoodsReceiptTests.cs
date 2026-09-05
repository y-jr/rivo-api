using Rivo.Audit.Contracts;
using Rivo.Procurement.Application.UseCases;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// Registar uma recepção de mercadoria.
///
/// <para>
/// <strong>A guarda que interessa é cumulativa</strong>, e é por isso que estes
/// testes existem. O domínio conhece a Ordem e conhece a Recepção, mas nunca as
/// vê em conjunto: quem soma o já recebido e o compara com o encomendado é o
/// caso de uso, lendo o armazenamento. Um teste sobre <c>GoodsReceipt</c> não
/// tem como saber o que chegou antes.
/// </para>
/// </summary>
public class RegisterGoodsReceiptTests
{
    private static readonly DateTimeOffset Agora = new(2026, 1, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider Relogio = new RelogioFixo(Agora);

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    private static RegisterGoodsReceipt Caso(
        FakeProcurementStore store,
        FakeEmployeeDirectory colaboradores,
        FakeAuditTrail trilha) =>
        new(store, colaboradores, trilha, Relogio);

    [Fact]
    public async Task Recebe_Dentro_Do_Encomendado()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var trilha = new FakeAuditTrail();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var linha = linhas[0];

        var resultado = await Caso(store, colaboradores, trilha).ExecuteAsync(
            ordem.Id, colaboradores.Existente(), null, "GR-1",
            [new NewGoodsReceiptLine(linha.Id, 40m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.Registered, resultado.Outcome);
        Assert.Equal(1, store.Gravacoes);
    }

    /// <summary>
    /// O caso central. Duas recepções que, isoladamente, cabem no encomendado —
    /// e que somadas o excedem. Nenhuma delas viola nada que o domínio veja.
    /// </summary>
    [Fact]
    public async Task Duas_Recepcoes_Que_Somadas_Excedem_E_Recusada_A_Segunda()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var trilha = new FakeAuditTrail();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var linha = linhas[0];
        var quemRecebe = colaboradores.Existente();

        // 60 cabem.
        var primeira = await Caso(store, colaboradores, trilha).ExecuteAsync(
            ordem.Id, quemRecebe, null, "GR-1",
            [new NewGoodsReceiptLine(linha.Id, 60m)], Actor(), CancellationToken.None);
        Assert.Equal(RegisterGoodsReceiptOutcome.Registered, primeira.Outcome);

        // Mais 50 também cabiam, se ninguém contasse as primeiras.
        var segunda = await Caso(store, colaboradores, trilha).ExecuteAsync(
            ordem.Id, quemRecebe, null, "GR-2",
            [new NewGoodsReceiptLine(linha.Id, 50m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.ExceedsOrdered, segunda.Outcome);

        // E a mensagem diz os três números que quem está no armazém precisa:
        // encomendado, já recebido, e o que falta.
        Assert.Contains("100", segunda.Error);
        Assert.Contains("60", segunda.Error);
        Assert.Contains("40", segunda.Error);

        // Só a primeira gravou.
        Assert.Equal(1, store.Gravacoes);
        Assert.Single(store.Recepcoes);
    }

    [Fact]
    public async Task Receber_Exactamente_O_Que_Falta_E_Aceite()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var linha = linhas[0];
        store.RecebidoAntes(ordem, linha.Id, 70m);

        var resultado = await Caso(store, colaboradores, new FakeAuditTrail()).ExecuteAsync(
            ordem.Id, colaboradores.Existente(), null, "GR-2",
            [new NewGoodsReceiptLine(linha.Id, 30m)], Actor(), CancellationToken.None);

        // A fronteira exacta é aceite: o excesso começa acima do que falta,
        // não em completar a encomenda.
        Assert.Equal(RegisterGoodsReceiptOutcome.Registered, resultado.Outcome);
    }

    /// <summary>
    /// Cada linha tem o seu acumulado. Uma linha cheia não deve impedir outra
    /// de receber, nem o contrário.
    /// </summary>
    [Fact]
    public async Task O_Acumulado_E_Por_Linha_E_Nao_Por_Ordem()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m), ("Areia", 50m, 2000m));
        var cimento = linhas[0];
        var areia = linhas[1];
        store.RecebidoAntes(ordem, cimento.Id, 100m); // cimento completo

        var resultado = await Caso(store, colaboradores, new FakeAuditTrail()).ExecuteAsync(
            ordem.Id, colaboradores.Existente(), null, "GR-2",
            [new NewGoodsReceiptLine(areia.Id, 50m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.Registered, resultado.Outcome);
    }

    [Fact]
    public async Task Linha_De_Outra_Ordem_E_Recusada()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var (_, linhasOutra) = store.Encomendar(("Tijolo", 200m, 1000m));

        var resultado = await Caso(store, colaboradores, new FakeAuditTrail()).ExecuteAsync(
            ordem.Id, colaboradores.Existente(), null, "GR-1",
            [new NewGoodsReceiptLine(linhasOutra[0].Id, 10m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.LineNotInOrder, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// Quem recebeu tem de existir em `hr`, e a verificação é uma chamada a
    /// outro módulo pelo contrato (ADR-010) — orquestração, não domínio.
    /// </summary>
    [Fact]
    public async Task Quem_Recebe_Tem_De_Existir_Em_Hr()
    {
        var store = new FakeProcurementStore();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));

        var resultado = await Caso(store, new FakeEmployeeDirectory(), new FakeAuditTrail()).ExecuteAsync(
            ordem.Id, Guid.NewGuid(), null, "GR-1",
            [new NewGoodsReceiptLine(linhas[0].Id, 10m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.ReceiverNotFound, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Ordem_Inexistente_Da_OrderNotFound()
    {
        var store = new FakeProcurementStore();

        var resultado = await Caso(store, new FakeEmployeeDirectory(), new FakeAuditTrail()).ExecuteAsync(
            Guid.NewGuid(), Guid.NewGuid(), null, null,
            [new NewGoodsReceiptLine(Guid.NewGuid(), 1m)], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.OrderNotFound, resultado.Outcome);
    }

    [Fact]
    public async Task Recepcao_Sem_Linhas_E_Recusada()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));

        var resultado = await Caso(store, colaboradores, new FakeAuditTrail()).ExecuteAsync(
            ordem.Id, colaboradores.Existente(), null, null,
            [], Actor(), CancellationToken.None);

        Assert.Equal(RegisterGoodsReceiptOutcome.Rejected, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// A recepção é a porta de entrada do stock, e a trilha tem de dizer contra
    /// que ordem e por ordem de quem — é o que liga a mercadoria ao 3-way match.
    /// </summary>
    [Fact]
    public async Task Recepcao_Auditada_Com_A_Ordem_E_Quem_Recebeu()
    {
        var store = new FakeProcurementStore();
        var colaboradores = new FakeEmployeeDirectory();
        var trilha = new FakeAuditTrail();
        var (ordem, linhas) = store.Encomendar(("Cimento", 100m, 5000m));
        var quemRecebe = colaboradores.Existente();

        await Caso(store, colaboradores, trilha).ExecuteAsync(
            ordem.Id, quemRecebe, null, "GR-77",
            [new NewGoodsReceiptLine(linhas[0].Id, 10m)], Actor(), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(ordem.Id.ToString(), registo.NewValue);
        Assert.Contains(quemRecebe.ToString(), registo.NewValue);
        Assert.Contains("GR-77", registo.NewValue);
    }
}
