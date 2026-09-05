using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// A ligação entre conta e colaborador (ADR-051).
///
/// <para>
/// Nenhuma destas regras vive no domínio, e é por isso que estes testes
/// existem: <c>Employee.LinkToUser</c> é um setter. Recusar a auto-ligação
/// depende do actor, e as duas unicidades dependem do armazenamento — as três
/// são orquestração, e um teste de domínio não lhes chega.
/// </para>
/// </summary>
public class LinkEmployeeAccountTests
{
    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    /// <summary>Instante fixo, para os episódios de histórico serem verificáveis.</summary>
    private static readonly DateTimeOffset Agora = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider Relogio = new RelogioFixo(Agora);

    [Fact]
    public async Task Liga_Colaborador_Sem_Conta()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Bento");
        var conta = Guid.NewGuid();

        var resultado = await new LinkEmployeeAccount(store, trilha, Relogio)
            .ExecuteAsync(colaborador.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.Linked, resultado.Outcome);
        Assert.Equal(conta, colaborador.UserId);
        Assert.Equal(1, store.Gravacoes);
    }

    /// <summary>
    /// O vínculo determina quem pode decidir aprovações (ADR-050). Quem o cria
    /// tem de deixar rasto, e o rasto tem de dizer <em>qual</em> conta.
    /// </summary>
    [Fact]
    public async Task Ligar_Fica_Na_Trilha_Com_A_Conta()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Bento");
        var conta = Guid.NewGuid();
        var quemLigou = Guid.NewGuid();

        await new LinkEmployeeAccount(store, trilha, Relogio)
            .ExecuteAsync(colaborador.Id, conta, Actor(quemLigou), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Equal(HrAuditActions.EmployeeAccountLinked, registo.Action);
        Assert.Equal(colaborador.Id.ToString(), registo.EntityId);
        Assert.Equal(quemLigou, registo.Context.ActorId);
        Assert.Contains(conta.ToString(), registo.NewValue);
    }

    /// <summary>
    /// O caminho de escalada mais directo: ligo a minha conta a um colaborador
    /// com Cargo de aprovação e passo a poder decidir. Recusado antes de
    /// sequer se saber se o colaborador existe.
    /// </summary>
    [Fact]
    public async Task Ligar_A_Propria_Conta_Recusado()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Bento");
        var euProprio = Guid.NewGuid();

        var resultado = await new LinkEmployeeAccount(store, trilha, Relogio)
            .ExecuteAsync(colaborador.Id, euProprio, Actor(euProprio), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.SelfLinkRefused, resultado.Outcome);
        Assert.Null(colaborador.UserId);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Ligar_A_Propria_Conta_Recusado_Mesmo_Com_Colaborador_Inexistente()
    {
        var store = new FakeHrStore();
        var euProprio = Guid.NewGuid();

        var resultado = await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(Guid.NewGuid(), euProprio, Actor(euProprio), CancellationToken.None);

        // A auto-ligação ganha ao 404 de propósito: a recusa não deve depender
        // de o atacante ter acertado num identificador válido.
        Assert.Equal(LinkEmployeeAccountOutcome.SelfLinkRefused, resultado.Outcome);
    }

    [Fact]
    public async Task Colaborador_Inexistente_Da_NotFound()
    {
        var store = new FakeHrStore();

        var resultado = await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.NotFound, resultado.Outcome);
    }

    /// <summary>
    /// Uma conta serve no máximo um colaborador — é o que a resolução de "o
    /// próprio" e a de "quem decide" passaram a confiar (ADR-042, ADR-050).
    /// </summary>
    [Fact]
    public async Task Conta_Ja_De_Outro_Colaborador_Da_Conflito()
    {
        var store = new FakeHrStore();
        var conta = Guid.NewGuid();
        store.Admitir("Ana Bento", conta);
        var outro = store.Admitir("Bruno Cabral");

        var resultado = await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(outro.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.UserAlreadyLinked, resultado.Outcome);
        Assert.Null(outro.UserId);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// Religar por cima transferiria a identidade com que se aprova. Recusa-se
    /// em vez de substituir — que é onde esta rota diverge de
    /// <c>LinkCustomerAccount</c>, que sobrepõe.
    /// </summary>
    [Fact]
    public async Task Colaborador_Ja_Com_Outra_Conta_Da_Conflito()
    {
        var store = new FakeHrStore();
        var contaAntiga = Guid.NewGuid();
        var colaborador = store.Admitir("Ana Bento", contaAntiga);

        var resultado = await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(colaborador.Id, Guid.NewGuid(), Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.EmployeeAlreadyLinked, resultado.Outcome);
        Assert.Equal(contaAntiga, colaborador.UserId);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// Repetir o mesmo pedido produz o mesmo estado e não enche a trilha de
    /// ligações que não mudaram nada.
    /// </summary>
    [Fact]
    public async Task Ligar_A_Mesma_Conta_De_Novo_E_Repetivel_Sem_Auditar()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var colaborador = store.Admitir("Ana Bento", conta);

        var resultado = await new LinkEmployeeAccount(store, trilha, Relogio)
            .ExecuteAsync(colaborador.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.Linked, resultado.Outcome);
        Assert.Empty(trilha.Registos);
        Assert.Equal(0, store.Gravacoes);
    }

    /// <summary>
    /// Um processo sem utilizador autenticado tem <c>ActorId</c> nulo. Nesse
    /// caso não há auto-ligação possível, e a verificação não pode confundir
    /// "sem actor" com "é o próprio".
    /// </summary>
    /// <summary>
    /// Ligar abre um episódio no histórico (ADR-053), na mesma transacção que
    /// o campo. As duas representações divergirem seria pior do que não haver
    /// histórico: uma investigação teria duas respostas e nenhuma forma de
    /// saber qual vale.
    /// </summary>
    [Fact]
    public async Task Ligar_Abre_Um_Episodio_No_Historico()
    {
        var store = new FakeHrStore();
        var colaborador = store.Admitir("Ana Bento");
        var conta = Guid.NewGuid();
        var quem = Guid.NewGuid();

        await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(colaborador.Id, conta, Actor(quem), CancellationToken.None);

        var episodio = Assert.Single(store.Episodios);
        Assert.Equal(colaborador.Id, episodio.EmployeeId);
        Assert.Equal(conta, episodio.UserId);
        Assert.Equal(Agora, episodio.LinkedOn);
        Assert.Equal(quem, episodio.LinkedByUserId);
        Assert.True(episodio.IsOpen);
    }

    [Fact]
    public async Task Ligacao_Recusada_Nao_Abre_Episodio()
    {
        var store = new FakeHrStore();
        var conta = Guid.NewGuid();
        store.Admitir("Ana Bento", conta);
        var outro = store.Admitir("Bruno Cabral");

        await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(outro.Id, conta, Actor(Guid.NewGuid()), CancellationToken.None);

        // Só o episódio que a admissão de Ana já tinha.
        Assert.Single(store.Episodios);
    }

    [Fact]
    public async Task Sem_Actor_A_Verificacao_De_Auto_Ligacao_Nao_Dispara()
    {
        var store = new FakeHrStore();
        var colaborador = store.Admitir("Ana Bento");
        var conta = Guid.NewGuid();

        var resultado = await new LinkEmployeeAccount(store, new FakeAuditTrail(), Relogio)
            .ExecuteAsync(colaborador.Id, conta, new AuditContext(null, null, null), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.Linked, resultado.Outcome);
    }
}
