using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.UseCases;
using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// Os casos de uso de conta.
///
/// <para>
/// <strong>O que vale a pena testar aqui não está em agregado nenhum:</strong>
/// é o efeito colateral que dá sentido a cada acto — mudar a password termina
/// as outras sessões, desactivar uma conta termina todas. Sem isso, os dois
/// actos ficavam a valer no papel e não na prática.
/// </para>
/// </summary>
public class ManageAccountsTests
{
    private static readonly Guid Utilizador = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static readonly AuditContext Contexto = new(Utilizador, "10.0.0.1", "corr-1");

    private static FakeUserAccounts ContaCom(string password)
    {
        var contas = new FakeUserAccounts();
        contas.Passwords[Utilizador] = password;

        return contas;
    }

    private static Session Sessao(Guid userId, DateTimeOffset criadaEm) =>
        Session.Start(userId, "10.0.0.1", "curl", criadaEm, TimeSpan.FromHours(1));

    // --- Mudar a própria password

    [Fact]
    public async Task ChangeOwnPassword_WithCorrectCurrent_Changes()
    {
        var contas = ContaCom("PasswordAntiga1");
        var sessoes = new FakeSessionStore();
        var trilha = new FakeAuditTrail();
        var relogio = new RelogioFixo(Agora);

        var resultado = await new ChangeOwnPassword(contas, sessoes, trilha, relogio)
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), "PasswordAntiga1", "PasswordNova1", Contexto, default);

        Assert.Equal(PasswordChangeResult.Changed, resultado.Result);
        Assert.Equal("PasswordNova1", contas.Passwords[Utilizador]);
        Assert.True(trilha.Has(AuditActions.PasswordChanged));
    }

    [Fact]
    public async Task ChangeOwnPassword_RevokesEveryOtherSession()
    {
        // **É a razão de o caso de uso existir.** Quem muda a password fá-lo
        // quase sempre por suspeitar que alguém a sabe — deixar as sessões
        // dessa pessoa abertas esvaziava o acto por completo.
        var contas = ContaCom("PasswordAntiga1");
        var sessoes = new FakeSessionStore();
        var relogio = new RelogioFixo(Agora);

        var corrente = Sessao(Utilizador, Agora);
        var outra = Sessao(Utilizador, Agora);
        var deOutraPessoa = Sessao(Guid.CreateVersion7(), Agora);

        await sessoes.AddAsync(corrente, default);
        await sessoes.AddAsync(outra, default);
        await sessoes.AddAsync(deOutraPessoa, default);

        await new ChangeOwnPassword(contas, sessoes, new FakeAuditTrail(), relogio)
            .ExecuteAsync(Utilizador, corrente.Id, "PasswordAntiga1", "PasswordNova1", Contexto, default);

        // A de quem mudou fica: seria estranho ser expulso por se ter protegido.
        Assert.True(corrente.IsActiveAt(Agora));
        Assert.False(outra.IsActiveAt(Agora));

        // E a de outra pessoa nunca esteve em causa.
        Assert.True(deOutraPessoa.IsActiveAt(Agora));
    }

    [Fact]
    public async Task ChangeOwnPassword_WithWrongCurrent_IsRefusedAndAudited()
    {
        // Uma sequência destas é a assinatura de quem tem o token e não tem a
        // credencial — por isso fica na trilha, e não só bloqueada.
        var contas = ContaCom("PasswordAntiga1");
        var trilha = new FakeAuditTrail();

        var resultado = await new ChangeOwnPassword(
                contas, new FakeSessionStore(), trilha, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), "EstaNaoE", "PasswordNova1", Contexto, default);

        Assert.Equal(PasswordChangeResult.WrongCurrentPassword, resultado.Result);
        Assert.Equal("PasswordAntiga1", contas.Passwords[Utilizador]);
        Assert.True(trilha.Has(AuditActions.PasswordChangeRefused));
        Assert.False(trilha.Has(AuditActions.PasswordChanged));
    }

    [Fact]
    public async Task ChangeOwnPassword_WithWrongCurrent_KeepsSessionsOpen()
    {
        // Uma tentativa falhada não pode expulsar ninguém: seria uma forma de
        // terminar as sessões de outra pessoa sem saber a password dela.
        var contas = ContaCom("PasswordAntiga1");
        var sessoes = new FakeSessionStore();
        var outra = Sessao(Utilizador, Agora);
        await sessoes.AddAsync(outra, default);

        await new ChangeOwnPassword(contas, sessoes, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), "EstaNaoE", "PasswordNova1", Contexto, default);

        Assert.True(outra.IsActiveAt(Agora));
    }

    [Fact]
    public async Task ChangeOwnPassword_WithWeakNewPassword_ReturnsTheReasons()
    {
        var contas = ContaCom("PasswordAntiga1");

        var resultado = await new ChangeOwnPassword(
                contas, new FakeSessionStore(), new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), "PasswordAntiga1", "curta", Contexto, default);

        Assert.Equal(PasswordChangeResult.Rejected, resultado.Result);
        Assert.NotEmpty(resultado.Errors);
        Assert.Equal("PasswordAntiga1", contas.Passwords[Utilizador]);
    }

    // --- Repor a password de outra conta

    [Fact]
    public async Task ResetUserPassword_RevokesEverySession()
    {
        // Ao contrário da mudança feita pelo próprio, aqui não há sessão a
        // poupar: quem administra não está dentro da conta.
        var contas = ContaCom("PasswordAntiga1");
        var sessoes = new FakeSessionStore();
        var uma = Sessao(Utilizador, Agora);
        var outra = Sessao(Utilizador, Agora);
        await sessoes.AddAsync(uma, default);
        await sessoes.AddAsync(outra, default);

        var trilha = new FakeAuditTrail();
        var avisos = new FakeNotifier();

        var resultado = await new ResetUserPassword(
                contas, sessoes, trilha, avisos, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, "PasswordNova1", Contexto, default);

        Assert.Equal(PasswordChangeResult.Changed, resultado.Result);
        Assert.False(uma.IsActiveAt(Agora));
        Assert.False(outra.IsActiveAt(Agora));
    }

    [Fact]
    public async Task ResetUserPassword_UsesItsOwnAuditAction()
    {
        // **Acção própria, e não uma mudança de password qualquer.** É o
        // caminho por onde uma conta é tomada, e quem audita tem de o encontrar
        // sem o procurar no meio das mudanças legítimas.
        var trilha = new FakeAuditTrail();

        await new ResetUserPassword(
                ContaCom("PasswordAntiga1"), new FakeSessionStore(), trilha,
                new FakeNotifier(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, "PasswordNova1", Contexto, default);

        Assert.True(trilha.Has(AuditActions.PasswordReset));
        Assert.False(trilha.Has(AuditActions.PasswordChanged));
    }

    [Fact]
    public async Task ResetUserPassword_NotifiesTheOwner()
    {
        // Se não foi o dono a pedir, é assim que ele fica a saber.
        var avisos = new FakeNotifier();

        await new ResetUserPassword(
                ContaCom("PasswordAntiga1"), new FakeSessionStore(), new FakeAuditTrail(),
                avisos, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, "PasswordNova1", Contexto, default);

        Assert.Single(avisos.Queued);
        Assert.Equal(Utilizador, avisos.Queued[0].RecipientUserId);
    }

    [Fact]
    public async Task ResetUserPassword_ForUnknownUser_DoesNothing()
    {
        var trilha = new FakeAuditTrail();
        var avisos = new FakeNotifier();

        var resultado = await new ResetUserPassword(
                new FakeUserAccounts(), new FakeSessionStore(), trilha,
                avisos, new RelogioFixo(Agora))
            .ExecuteAsync(Guid.CreateVersion7(), "PasswordNova1", Contexto, default);

        Assert.Equal(PasswordChangeResult.UserNotFound, resultado.Result);
        Assert.Empty(trilha.Records);
        Assert.Empty(avisos.Queued);
    }

    // --- Activar e desactivar

    [Fact]
    public async Task SetAccountStatus_Deactivating_RevokesEverySession()
    {
        // **É o que faltava para cortar o acesso a quem sai da empresa.** Sem
        // isto, a conta ficava fechada à entrada e aberta por dentro, até o
        // último token expirar.
        var contas = ContaCom("Password1");
        var sessoes = new FakeSessionStore();
        var aberta = Sessao(Utilizador, Agora);
        await sessoes.AddAsync(aberta, default);

        var trilha = new FakeAuditTrail();

        var resultado = await new SetAccountStatus(contas, sessoes, trilha, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, active: false, "Saiu da empresa.", Contexto, default);

        Assert.Equal(AccountStatusOutcome.Changed, resultado);
        Assert.Contains(Utilizador, contas.Deactivated);
        Assert.False(aberta.IsActiveAt(Agora));
        Assert.True(trilha.Has(AuditActions.AccountDeactivated));
    }

    [Fact]
    public async Task SetAccountStatus_Reactivating_DoesNotTouchSessions()
    {
        // Reactivar devolve a possibilidade de entrar, não as sessões antigas —
        // essas foram terminadas e não voltam.
        var contas = ContaCom("Password1");
        var sessoes = new FakeSessionStore();
        var aberta = Sessao(Utilizador, Agora);
        await sessoes.AddAsync(aberta, default);

        var trilha = new FakeAuditTrail();

        await new SetAccountStatus(contas, sessoes, trilha, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, active: true, "Voltou.", Contexto, default);

        Assert.DoesNotContain(Utilizador, contas.Deactivated);
        Assert.True(aberta.IsActiveAt(Agora));
        Assert.True(trilha.Has(AuditActions.AccountReactivated));
    }

    [Fact]
    public async Task SetAccountStatus_KeepsTheReasonInTheTrail()
    {
        // Fechar o acesso de alguém sem dizer porquê deixa quem audita a olhar
        // para um registo que não explica nada.
        var trilha = new FakeAuditTrail();

        await new SetAccountStatus(
                ContaCom("Password1"), new FakeSessionStore(), trilha, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, active: false, "Saiu da empresa.", Contexto, default);

        var registo = trilha.Records.Single(r => r.Action == AuditActions.AccountDeactivated);
        Assert.Contains("Saiu da empresa.", registo.NewValue);
    }

    [Fact]
    public async Task SetAccountStatus_ForUnknownUser_DoesNothing()
    {
        var trilha = new FakeAuditTrail();

        var resultado = await new SetAccountStatus(
                new FakeUserAccounts(), new FakeSessionStore(), trilha, new RelogioFixo(Agora))
            .ExecuteAsync(Guid.CreateVersion7(), active: false, "Qualquer.", Contexto, default);

        Assert.Equal(AccountStatusOutcome.UserNotFound, resultado);
        Assert.Empty(trilha.Records);
    }

    // --- Retirar perfil

    [Fact]
    public async Task RemoveAccessProfile_RemovesAndAudits()
    {
        var contas = ContaCom("Password1");
        await contas.AssignProfileAsync(Utilizador, "Finance", default);
        var trilha = new FakeAuditTrail();

        var resultado = await new RemoveAccessProfile(contas, trilha)
            .ExecuteAsync(Utilizador, "Finance", Contexto, default);

        Assert.Equal(AssignProfileOutcome.Assigned, resultado);
        Assert.DoesNotContain("Finance", contas.Profiles[Utilizador]);
        Assert.True(trilha.Has(AuditActions.ProfileRemoved));
    }

    [Fact]
    public async Task RemoveAccessProfile_ThatWasNotAssigned_Succeeds()
    {
        // Repetível sem erro, como a atribuição: o estado pretendido é o mesmo.
        var contas = ContaCom("Password1");

        var resultado = await new RemoveAccessProfile(contas, new FakeAuditTrail())
            .ExecuteAsync(Utilizador, "Finance", Contexto, default);

        Assert.Equal(AssignProfileOutcome.Assigned, resultado);
    }

    // --- Sessões

    [Fact]
    public async Task ListOwnSessions_MarksTheCurrentOne()
    {
        // Marcar a corrente evita o engano mais fácil desta lista: terminar a
        // sessão de onde se está a olhar para ela.
        var sessoes = new FakeSessionStore();
        var corrente = Sessao(Utilizador, Agora);
        var outra = Sessao(Utilizador, Agora.AddHours(-2));
        await sessoes.AddAsync(corrente, default);
        await sessoes.AddAsync(outra, default);

        var vista = await new ListOwnSessions(sessoes, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, corrente.Id, default);

        Assert.Equal(2, vista.Count);
        Assert.True(vista.Single(s => s.SessionId == corrente.Id).IsCurrent);
        Assert.False(vista.Single(s => s.SessionId == outra.Id).IsCurrent);

        // A de há duas horas já expirou — a duração é de uma hora.
        Assert.False(vista.Single(s => s.SessionId == outra.Id).IsActive);
    }

    [Fact]
    public async Task ListOwnSessions_ShowsOnlyTheOwnersSessions()
    {
        var sessoes = new FakeSessionStore();
        await sessoes.AddAsync(Sessao(Utilizador, Agora), default);
        await sessoes.AddAsync(Sessao(Guid.CreateVersion7(), Agora), default);

        var vista = await new ListOwnSessions(sessoes, new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), default);

        Assert.Single(vista);
    }

    [Fact]
    public async Task RevokeOwnSession_Revokes()
    {
        var sessoes = new FakeSessionStore();
        var sessao = Sessao(Utilizador, Agora);
        await sessoes.AddAsync(sessao, default);

        var resultado = await new RevokeOwnSession(sessoes, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, sessao.Id, Contexto, default);

        Assert.Equal(RevokeSessionOutcome.Revoked, resultado);
        Assert.False(sessao.IsActiveAt(Agora));
    }

    [Fact]
    public async Task RevokeOwnSession_OfAnotherUser_IsNotFoundAndDoesNotRevoke()
    {
        // **A regra que mais custa se falhar.** Devolver "não encontrada" em vez
        // de "não é sua" evita confirmar a existência de um identificador a quem
        // não tem nada a ver com ele — e, sobretudo, não a revoga.
        var sessoes = new FakeSessionStore();
        var deOutraPessoa = Sessao(Guid.CreateVersion7(), Agora);
        await sessoes.AddAsync(deOutraPessoa, default);

        var resultado = await new RevokeOwnSession(sessoes, new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, deOutraPessoa.Id, Contexto, default);

        Assert.Equal(RevokeSessionOutcome.NotFound, resultado);
        Assert.True(deOutraPessoa.IsActiveAt(Agora));
    }

    [Fact]
    public async Task RevokeOwnSession_ThatDoesNotExist_IsNotFound()
    {
        var resultado = await new RevokeOwnSession(
                new FakeSessionStore(), new FakeAuditTrail(), new RelogioFixo(Agora))
            .ExecuteAsync(Utilizador, Guid.CreateVersion7(), Contexto, default);

        Assert.Equal(RevokeSessionOutcome.NotFound, resultado);
    }
}
