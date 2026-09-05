using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.UseCases;

namespace Rivo.Fleet.Application.Tests;

/// <summary>
/// Atribuir uma viatura a um colaborador.
///
/// <para>
/// A regra de «uma viatura, uma atribuição aberta» vive no agregado e está
/// coberta no domínio. O que só o caso de uso vê é que <strong>o colaborador
/// existe</strong> — vive em `hr`, e chega por contrato (ADR-010).
/// </para>
/// </summary>
public class AssignVehicleTests
{
    private static readonly DateOnly Dia = new(2026, 6, 1);
    private static readonly TimeProvider Relogio =
        new RelogioFixo(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    [Fact]
    public async Task Atribui_A_Colaborador_Existente()
    {
        var store = new FakeVehicleStore();
        var colaboradores = new FakeEmployeeDirectory();
        var trilha = new FakeAuditTrail();
        var viatura = store.Registar("LD-01-AA");
        var condutor = colaboradores.Existente();

        var resultado = await new AssignVehicle(store, colaboradores, trilha, Relogio)
            .ExecuteAsync(viatura.Id, condutor, Dia, Actor(), CancellationToken.None);

        Assert.Equal(AssignVehicleOutcome.Assigned, resultado.Outcome);
        Assert.Equal(1, store.Gravacoes);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(condutor.ToString(), registo.NewValue);
    }

    /// <summary>
    /// Um colaborador que não existe em `hr` não conduz nada. Sem esta
    /// verificação a viatura ficaria atribuída a um identificador sem pessoa
    /// por trás — e o registo de quem conduzia deixaria de ser legível.
    /// </summary>
    [Fact]
    public async Task Colaborador_Inexistente_Em_Hr_E_Recusado()
    {
        var store = new FakeVehicleStore();
        var trilha = new FakeAuditTrail();
        var viatura = store.Registar("LD-01-AA");

        var resultado = await new AssignVehicle(store, new FakeEmployeeDirectory(), trilha, Relogio)
            .ExecuteAsync(viatura.Id, Guid.NewGuid(), Dia, Actor(), CancellationToken.None);

        Assert.Equal(AssignVehicleOutcome.EmployeeNotFound, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
        Assert.Empty(trilha.Registos);
    }

    /// <summary>
    /// Viatura inexistente e colaborador inexistente são desfechos distintos —
    /// e a viatura é procurada primeiro, o que evita ir a `hr` por nada.
    /// </summary>
    [Fact]
    public async Task Viatura_Inexistente_Distingue_Se_E_Nem_Chega_A_Hr()
    {
        var store = new FakeVehicleStore();

        // O directório lança em FindAsync? Não — devolve nulo para desconhecidos.
        // O que se verifica aqui é o desfecho, não a chamada.
        var resultado = await new AssignVehicle(store, new FakeEmployeeDirectory(), new FakeAuditTrail(), Relogio)
            .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Dia, Actor(), CancellationToken.None);

        Assert.Equal(AssignVehicleOutcome.VehicleNotFound, resultado.Outcome);
    }

    /// <summary>
    /// Viatura já atribuída dá conflito, não pedido malformado: o estado é que
    /// impede, e a distinção 409/400 é o que diz a quem chama se vale a pena
    /// corrigir o pedido ou o estado.
    /// </summary>
    [Fact]
    public async Task Viatura_Ja_Atribuida_Da_Conflito()
    {
        var store = new FakeVehicleStore();
        var colaboradores = new FakeEmployeeDirectory();
        var viatura = store.Registar("LD-01-AA");
        var caso = new AssignVehicle(store, colaboradores, new FakeAuditTrail(), Relogio);

        await caso.ExecuteAsync(viatura.Id, colaboradores.Existente(), Dia, Actor(), CancellationToken.None);

        var segunda = await caso.ExecuteAsync(
            viatura.Id, colaboradores.Existente(), Dia, Actor(), CancellationToken.None);

        Assert.Equal(AssignVehicleOutcome.Conflict, segunda.Outcome);
    }

    [Fact]
    public async Task Viatura_Inactiva_Da_Conflito()
    {
        var store = new FakeVehicleStore();
        var colaboradores = new FakeEmployeeDirectory();
        var viatura = store.Registar("LD-01-AA", activa: false);

        var resultado = await new AssignVehicle(store, colaboradores, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(viatura.Id, colaboradores.Existente(), Dia, Actor(), CancellationToken.None);

        Assert.Equal(AssignVehicleOutcome.Conflict, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
    }
}
