using Rivo.Audit.Contracts;
using Rivo.Commercial.Application.UseCases;

namespace Rivo.Commercial.Application.Tests;

/// <summary>
/// Ligar e desligar a conta de um Cliente (ADR-055).
///
/// <para>
/// O caso que dá nome a este ficheiro é
/// <see cref="Religar_Por_Cima_E_Recusado_Nao_Substitui"/>: até 2026-09-05,
/// ligar uma conta a um cliente que já tinha outra <strong>substituía-a em
/// silêncio</strong>. O portal mudava de dono sem registo, e a conta anterior
/// perdia o acesso sem explicação.
/// </para>
/// </summary>
public class LinkCustomerAccountTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider Relogio = new RelogioFixo(Agora);

    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    [Fact]
    public async Task Liga_Cliente_Sem_Conta()
    {
        var store = new FakeCustomerStore();
        var cliente = store.Registar("Padaria Central");
        var conta = Guid.NewGuid();

        var resultado = await new LinkCustomerAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(cliente.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkCustomerAccountOutcome.Linked, resultado.Outcome);
        Assert.Equal(conta, cliente.UserId);
    }

    /// <summary>
    /// <strong>A correcção do ADR-055.</strong> Antes, isto substituía o
    /// vínculo sem recusar nem distinguir na trilha.
    /// </summary>
    [Fact]
    public async Task Religar_Por_Cima_E_Recusado_Nao_Substitui()
    {
        var store = new FakeCustomerStore();
        var contaAntiga = Guid.NewGuid();
        var cliente = store.Registar("Padaria Central", contaAntiga);

        var resultado = await new LinkCustomerAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(cliente.Id, Guid.NewGuid(), Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkCustomerAccountOutcome.CustomerAlreadyLinked, resultado.Outcome);
        Assert.Equal(contaAntiga, cliente.UserId);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// Recusada não por simetria com `hr`, mas por consequência própria: quem
    /// ligue a sua conta a um cliente passa a poder submeter comprovativos de
    /// pagamento como esse cliente (ADR-044).
    /// </summary>
    [Fact]
    public async Task Ligar_A_Propria_Conta_Recusado()
    {
        var store = new FakeCustomerStore();
        var cliente = store.Registar("Padaria Central");
        var euProprio = Guid.NewGuid();

        var resultado = await new LinkCustomerAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(cliente.Id, euProprio, Actor(euProprio), CancellationToken.None);

        Assert.Equal(LinkCustomerAccountOutcome.SelfLinkRefused, resultado.Outcome);
        Assert.Null(cliente.UserId);
    }

    [Fact]
    public async Task Conta_Ja_De_Outro_Cliente_Da_Conflito()
    {
        var store = new FakeCustomerStore();
        var conta = Guid.NewGuid();
        store.Registar("Padaria Central", conta);
        var outro = store.Registar("Mercearia do Bairro");

        var resultado = await new LinkCustomerAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(outro.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkCustomerAccountOutcome.UserAlreadyLinked, resultado.Outcome);
        Assert.Null(outro.UserId);
    }

    [Fact]
    public async Task Ligar_A_Mesma_Conta_De_Novo_E_Repetivel_Sem_Auditar()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var cliente = store.Registar("Padaria Central", conta);

        var resultado = await new LinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkCustomerAccountOutcome.Linked, resultado.Outcome);
        Assert.Empty(trilha.Registos);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Ligar_Abre_Episodio_E_Audita_Com_A_Conta()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var cliente = store.Registar("Padaria Central");
        var conta = Guid.NewGuid();
        var quem = Guid.NewGuid();

        await new LinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, conta, Actor(quem), CancellationToken.None);

        var episodio = Assert.Single(store.Episodios);
        Assert.Equal(conta, episodio.UserId);
        Assert.Equal(Agora, episodio.LinkedOn);
        Assert.Equal(quem, episodio.LinkedByUserId);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(conta.ToString(), registo.NewValue);
    }

    // ── desligar ──

    [Fact]
    public async Task Desligar_Fecha_O_Episodio_E_Liberta_O_Cliente()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var cliente = store.Registar("Padaria Central", conta);
        var quem = Guid.NewGuid();

        var resultado = await new UnlinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, Actor(quem), CancellationToken.None);

        Assert.Equal(UnlinkCustomerAccountOutcome.Unlinked, resultado.Outcome);
        Assert.Null(cliente.UserId);

        var episodio = Assert.Single(store.Episodios);
        Assert.False(episodio.IsOpen);
        Assert.Equal(quem, episodio.UnlinkedByUserId);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains(conta.ToString(), registo.PreviousValue);
    }

    [Fact]
    public async Task Desligar_Quem_Nao_Tem_Conta_E_Repetivel_Sem_Auditar()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var cliente = store.Registar("Padaria Central");

        var resultado = await new UnlinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(UnlinkCustomerAccountOutcome.Unlinked, resultado.Outcome);
        Assert.Empty(trilha.Registos);
    }

    /// <summary>
    /// A sequência que corrige um vínculo errado, e a única que existe desde
    /// que religar por cima passou a ser recusado.
    /// </summary>
    [Fact]
    public async Task Desligar_E_Religar_A_Outro_Deixa_A_Transferencia_Legivel()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var errado = store.Registar("Cliente Errado", conta);
        var certo = store.Registar("Cliente Certo");
        var actor = Guid.NewGuid();

        await new UnlinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(errado.Id, Actor(actor), CancellationToken.None);
        await new LinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(certo.Id, conta, Actor(actor), CancellationToken.None);

        Assert.Null(errado.UserId);
        Assert.Equal(conta, certo.UserId);

        Assert.Equal(2, trilha.Registos.Count);
        Assert.Contains(conta.ToString(), trilha.Registos[0].PreviousValue);
        Assert.Contains(conta.ToString(), trilha.Registos[1].NewValue);
    }

    [Fact]
    public async Task O_Historico_Responde_A_Quem_Podia_Agir_E_Quando()
    {
        var store = new FakeCustomerStore();
        var trilha = new FakeAuditTrail();
        var primeira = Guid.NewGuid();
        var segunda = Guid.NewGuid();
        var cliente = store.Registar("Padaria Central", primeira);
        var actor = Guid.NewGuid();

        await new UnlinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, Actor(actor), CancellationToken.None);
        await new LinkCustomerAccount(store, trilha, Relogio)
            .ExecuteAsync(cliente.Id, segunda, Actor(actor), CancellationToken.None);

        var historico = await new GetCustomerAccountHistory(store)
            .ExecuteAsync(cliente.Id, CancellationToken.None);

        Assert.NotNull(historico);
        Assert.Equal(2, historico.Count);
        Assert.Equal(segunda, historico[0].UserId);
        Assert.Null(historico[0].UnlinkedOn);
        Assert.Equal(primeira, historico[1].UserId);
        Assert.Equal(Agora, historico[1].UnlinkedOn);
    }

    [Fact]
    public async Task Cliente_Sem_Conta_Da_Lista_Vazia_E_Inexistente_Da_Nulo()
    {
        var store = new FakeCustomerStore();
        var semConta = store.Registar("Padaria Central");
        var caso = new GetCustomerAccountHistory(store);

        Assert.Empty((await caso.ExecuteAsync(semConta.Id, CancellationToken.None))!);
        Assert.Null(await caso.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
