using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// #39 do levantamento de pendências: nada impedia várias pessoas ocuparem o
/// mesmo Cargo em simultâneo. Estes testes cobrem a regra nova — encerramento
/// sempre explícito, nunca automático.
/// </summary>
public class AssignPositionTests
{
    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    private static readonly DateTimeOffset Agora = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider Relogio = new RelogioFixo(Agora);

    private static AssignPosition NovoCasoDeUso(FakeHrStore store, FakeAuditTrail trilha) =>
        new(store, trilha, new FakeHrApprovalSubmission());

    [Fact]
    public async Task Atribui_Cargo_Livre()
    {
        var store = new FakeHrStore();
        var colaborador = store.Admitir("Ana Bento");
        var cargo = store.CriarCargo("Contabilista");

        var resultado = await NovoCasoDeUso(store, new FakeAuditTrail())
            .ExecuteAsync(colaborador.Id, cargo.Id, Agora, null, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssignPositionOutcome.Assigned, resultado.Outcome);
    }

    /// <summary>
    /// Cargo sem autoridade — vários colaboradores já o ocupam em simultâneo de
    /// propósito (ex. vários "Contabilista"), e a regra nova não mexe nisso.
    /// </summary>
    [Fact]
    public async Task Permite_Varios_Ocupantes_De_Cargo_Sem_Autoridade()
    {
        var store = new FakeHrStore();
        var jaOcupava = store.Admitir("Ana Bento");
        var candidato = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("Contabilista");
        store.AtribuirCargo(jaOcupava.Id, cargo.Id, Agora.AddYears(-1));

        var resultado = await NovoCasoDeUso(store, new FakeAuditTrail())
            .ExecuteAsync(candidato.Id, cargo.Id, Agora, null, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssignPositionOutcome.Assigned, resultado.Outcome);
    }

    /// <summary>
    /// Submeter um candidato a um Cargo com autoridade já ocupado é o caso
    /// normal de rever quem sucede a quem — não é o problema do #39, e por
    /// isso não se recusa aqui (fica Pending, como qualquer outra submissão).
    /// </summary>
    [Fact]
    public async Task Permite_Submeter_Candidato_A_Cargo_Com_Autoridade_Ja_Ocupado()
    {
        var store = new FakeHrStore();
        var jaOcupava = store.Admitir("Ana Bento");
        var candidato = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("CEO", confereAutoridade: true);
        store.AtribuirCargo(jaOcupava.Id, cargo.Id, Agora.AddYears(-1));

        var resultado = await NovoCasoDeUso(store, new FakeAuditTrail())
            .ExecuteAsync(candidato.Id, cargo.Id, Agora, null, Actor(Guid.NewGuid()), CancellationToken.None);

        // FakeHrApprovalSubmission.IsAvailable é falso, por isso o desfecho
        // concreto é ApprovalUnavailable e não PendingApproval — o que importa
        // aqui é que NÃO seja PositionOccupied.
        Assert.NotEqual(AssignPositionOutcome.PositionOccupied, resultado.Outcome);
    }

    /// <summary>
    /// O caso relatado: três "CEO" ao mesmo tempo. Aqui é onde de facto
    /// aconteceria — aprovar uma segunda pendente sem nunca ter encerrado a
    /// primeira.
    /// </summary>
    [Fact]
    public async Task Aprovar_Nao_Promove_Cargo_Com_Autoridade_Ja_Ocupado()
    {
        var store = new FakeHrStore();
        var jaOcupava = store.Admitir("Ana Bento");
        var candidato = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("CEO", confereAutoridade: true);
        store.AtribuirCargo(jaOcupava.Id, cargo.Id, Agora.AddYears(-1));
        var requestId = Guid.NewGuid();
        var pendente = store.AtribuirCargoPendente(candidato.Id, cargo.Id, Agora, requestId);

        var resultado = await new ApplyPositionApprovalOutcome(
                store, new FakeApprovalOutcome(HrApprovalState.Approved), new FakeAuditTrail(), Relogio)
            .ExecuteAsync(pendente.Id, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ApplyApprovalOutcome.Blocked, resultado.Outcome);
        Assert.Equal(PositionAssignmentStatus.Pending, pendente.Status);
    }

    /// <summary>Uma atribuição já terminada não bloqueia uma nova, mesmo com autoridade.</summary>
    [Fact]
    public async Task Permite_Atribuir_Cargo_Com_Autoridade_Cuja_Ocupacao_Anterior_Ja_Terminou()
    {
        var store = new FakeHrStore();
        var antigo = store.Admitir("Ana Bento");
        var novo = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("CEO", confereAutoridade: true);
        store.AtribuirCargo(antigo.Id, cargo.Id, Agora.AddYears(-1), Agora.AddMonths(-1));

        // ExecuteDirectAsync (ADR-058), não ExecuteAsync: um Cargo com autoridade
        // passa por `approval` no caminho normal, e este teste isola só a regra
        // de ocupação — não o fluxo de governança, coberto noutro lado.
        var resultado = await NovoCasoDeUso(store, new FakeAuditTrail())
            .ExecuteDirectAsync(novo.Id, cargo.Id, Agora, null, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssignPositionOutcome.Assigned, resultado.Outcome);
    }

    /// <summary>Encerrar liberta o Cargo — é o desenho: sempre explícito, nunca automático.</summary>
    [Fact]
    public async Task Encerrar_Liberta_O_Cargo_Com_Autoridade_Para_Nova_Atribuicao()
    {
        var store = new FakeHrStore();
        var antigo = store.Admitir("Ana Bento");
        var novo = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("CEO", confereAutoridade: true);
        var atribuicaoAntiga = store.AtribuirCargo(antigo.Id, cargo.Id, Agora.AddYears(-1));

        var encerramento = await new EndPositionAssignment(store, new FakeAuditTrail())
            .ExecuteAsync(atribuicaoAntiga.Id, Agora, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(PositionAssignmentClosureOutcome.Ended, encerramento.Outcome);

        var resultado = await NovoCasoDeUso(store, new FakeAuditTrail())
            .ExecuteDirectAsync(novo.Id, cargo.Id, Agora, null, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AssignPositionOutcome.Assigned, resultado.Outcome);
    }

    [Fact]
    public async Task Encerrar_Fica_Na_Trilha()
    {
        var store = new FakeHrStore();
        var colaborador = store.Admitir("Ana Bento");
        var cargo = store.CriarCargo("CEO");
        var atribuicao = store.AtribuirCargo(colaborador.Id, cargo.Id, Agora.AddYears(-1));
        var trilha = new FakeAuditTrail();
        var quemEncerrou = Guid.NewGuid();

        await new EndPositionAssignment(store, trilha)
            .ExecuteAsync(atribuicao.Id, Agora, Actor(quemEncerrou), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Equal(HrAuditActions.PositionAssignmentEnded, registo.Action);
        Assert.Equal(quemEncerrou, registo.Context.ActorId);
    }

    [Fact]
    public async Task Encerrar_Atribuicao_Inexistente_Devolve_NotFound()
    {
        var store = new FakeHrStore();

        var resultado = await new EndPositionAssignment(store, new FakeAuditTrail())
            .ExecuteAsync(Guid.NewGuid(), Agora, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(PositionAssignmentClosureOutcome.NotFound, resultado.Outcome);
    }

    /// <summary>Encerrar duas vezes é conflito, não sucesso silencioso.</summary>
    [Fact]
    public async Task Encerrar_Duas_Vezes_E_Recusado()
    {
        var store = new FakeHrStore();
        var colaborador = store.Admitir("Ana Bento");
        var cargo = store.CriarCargo("CEO");
        var atribuicao = store.AtribuirCargo(colaborador.Id, cargo.Id, Agora.AddYears(-1));
        var useCase = new EndPositionAssignment(store, new FakeAuditTrail());

        await useCase.ExecuteAsync(atribuicao.Id, Agora, Actor(Guid.NewGuid()), CancellationToken.None);
        var segunda = await useCase.ExecuteAsync(atribuicao.Id, Agora, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(PositionAssignmentClosureOutcome.Rejected, segunda.Outcome);
    }

    /// <summary>
    /// Sem isto (#39), encerrar uma atribuição exigia ir à base de dados
    /// buscar o assignmentId — o encerramento existia sem forma de o
    /// descobrir.
    /// </summary>
    [Fact]
    public async Task Lista_Atribuicoes_Do_Cargo_Do_Mais_Recente_Para_O_Mais_Antigo()
    {
        var store = new FakeHrStore();
        var antigo = store.Admitir("Ana Bento");
        var actual = store.Admitir("Bruno Costa");
        var cargo = store.CriarCargo("CEO");
        var atribuicaoAntiga = store.AtribuirCargo(antigo.Id, cargo.Id, Agora.AddYears(-2), Agora.AddYears(-1));
        var atribuicaoActual = store.AtribuirCargo(actual.Id, cargo.Id, Agora.AddYears(-1));

        var lista = await new ListPositionAssignments(store).ExecuteAsync(cargo.Id, CancellationToken.None);

        Assert.Equal(2, lista.Count);
        Assert.Equal(atribuicaoActual.Id, lista[0].AssignmentId);
        Assert.Equal(atribuicaoAntiga.Id, lista[1].AssignmentId);
        Assert.Null(lista[0].EffectiveTo);
        Assert.NotNull(lista[1].EffectiveTo);
    }

    [Fact]
    public async Task Lista_Atribuicoes_De_Cargo_Sem_Nenhuma_Devolve_Vazio()
    {
        var store = new FakeHrStore();

        var lista = await new ListPositionAssignments(store).ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(lista);
    }
}
