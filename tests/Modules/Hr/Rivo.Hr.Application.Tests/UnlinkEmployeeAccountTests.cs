using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Desligar a conta de um colaborador (ADR-052).
/// </summary>
public class UnlinkEmployeeAccountTests
{
    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    [Fact]
    public async Task Desliga_Colaborador_Com_Conta()
    {
        var store = new FakeHrStore();
        var conta = Guid.NewGuid();
        var colaborador = store.Admitir("Ana Bento", conta);

        var resultado = await new UnlinkEmployeeAccount(store, new FakeAuditTrail())
            .ExecuteAsync(colaborador.Id, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(UnlinkEmployeeAccountOutcome.Unlinked, resultado.Outcome);
        Assert.Null(colaborador.UserId);
        Assert.Equal(1, store.Gravacoes);
    }

    /// <summary>
    /// A conta removida fica em <c>PreviousValue</c>. É o que torna legível
    /// uma transferência feita em dois passos — desligar e voltar a ligar —
    /// que é o preço assumido por o desligar existir.
    /// </summary>
    [Fact]
    public async Task Desligar_Guarda_A_Conta_Removida_Na_Trilha()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var quemDesligou = Guid.NewGuid();
        var colaborador = store.Admitir("Ana Bento", conta);

        await new UnlinkEmployeeAccount(store, trilha)
            .ExecuteAsync(colaborador.Id, Actor(quemDesligou), CancellationToken.None);

        var registo = Assert.Single(trilha.Registos);
        Assert.Equal(HrAuditActions.EmployeeAccountUnlinked, registo.Action);
        Assert.Equal(colaborador.Id.ToString(), registo.EntityId);
        Assert.Equal(quemDesligou, registo.Context.ActorId);
        Assert.Contains(conta.ToString(), registo.PreviousValue);
        Assert.Null(registo.NewValue);
    }

    [Fact]
    public async Task Desligar_Quem_Nao_Tem_Conta_E_Repetivel_Sem_Auditar()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Bento");

        var resultado = await new UnlinkEmployeeAccount(store, trilha)
            .ExecuteAsync(colaborador.Id, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(UnlinkEmployeeAccountOutcome.Unlinked, resultado.Outcome);
        Assert.Empty(trilha.Registos);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Colaborador_Inexistente_Da_NotFound()
    {
        var store = new FakeHrStore();

        var resultado = await new UnlinkEmployeeAccount(store, new FakeAuditTrail())
            .ExecuteAsync(Guid.NewGuid(), Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(UnlinkEmployeeAccountOutcome.NotFound, resultado.Outcome);
    }

    /// <summary>
    /// Desligar a própria conta é permitido, ao contrário de ligar. É
    /// estritamente uma perda de capacidade, e não encadeia em escalada:
    /// voltar a ligar-se a outro colaborador continua a exigir outra pessoa.
    /// </summary>
    [Fact]
    public async Task Desligar_A_Propria_Conta_E_Permitido()
    {
        var store = new FakeHrStore();
        var euProprio = Guid.NewGuid();
        var colaborador = store.Admitir("Ana Bento", euProprio);

        var resultado = await new UnlinkEmployeeAccount(store, new FakeAuditTrail())
            .ExecuteAsync(colaborador.Id, Actor(euProprio), CancellationToken.None);

        Assert.Equal(UnlinkEmployeeAccountOutcome.Unlinked, resultado.Outcome);
        Assert.Null(colaborador.UserId);
    }

    /// <summary>
    /// Depois de desligar, a conta fica livre para outro colaborador — e o
    /// colaborador fica livre para outra conta. É a sequência que corrige um
    /// vínculo errado, e a única que existe para isso.
    /// </summary>
    [Fact]
    public async Task Desligado_O_Vinculo_Pode_Ser_Refeito_Para_Outro()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var conta = Guid.NewGuid();
        var errado = store.Admitir("Pessoa Errada", conta);
        var certo = store.Admitir("Pessoa Certa");
        var actor = Guid.NewGuid();

        await new UnlinkEmployeeAccount(store, trilha)
            .ExecuteAsync(errado.Id, Actor(actor), CancellationToken.None);

        var refazer = await new LinkEmployeeAccount(store, trilha)
            .ExecuteAsync(certo.Id, conta, Actor(actor), CancellationToken.None);

        Assert.Equal(LinkEmployeeAccountOutcome.Linked, refazer.Outcome);
        Assert.Null(errado.UserId);
        Assert.Equal(conta, certo.UserId);

        // Dois registos, e o par diz o que aconteceu: a conta saiu de um e
        // entrou noutro. É a transferência a ficar legível.
        Assert.Equal(2, trilha.Registos.Count);
        Assert.Equal(HrAuditActions.EmployeeAccountUnlinked, trilha.Registos[0].Action);
        Assert.Equal(HrAuditActions.EmployeeAccountLinked, trilha.Registos[1].Action);
        Assert.Contains(conta.ToString(), trilha.Registos[0].PreviousValue);
        Assert.Contains(conta.ToString(), trilha.Registos[1].NewValue);
    }
}
