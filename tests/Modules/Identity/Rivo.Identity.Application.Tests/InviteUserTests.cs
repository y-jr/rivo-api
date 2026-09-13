using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.UseCases;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// O convite substitui o registo público (ADR-059). O que estes testes
/// protegem é a razão de ele existir: quem entra é quem foi convidado, e o
/// testemunho sai pelo correio e não pela resposta.
/// </summary>
public sealed class InviteUserTests
{
    private static readonly AuditContext Contexto = new(Guid.NewGuid(), "::1", "correlacao");

    private const string LinkBase = "https://rivo-lac.vercel.app";

    [Fact]
    public async Task Convite_cria_a_conta_e_atribui_o_perfil()
    {
        var contas = new FakeUserAccounts();
        var notificador = new FakeNotifier();
        var convidar = new InviteUser(contas, new FakeAuditTrail(), notificador);

        var resultado = await convidar.ExecuteAsync(
            "novo@rivo.ao", "HR", LinkBase, Contexto, CancellationToken.None);

        Assert.Equal(InviteUserOutcome.Invited, resultado.Outcome);
        Assert.Equal("novo@rivo.ao", Assert.Single(contas.Invited));
        Assert.Equal("HR", Assert.Single(contas.AssignedProfiles));
    }

    [Fact]
    public async Task O_testemunho_vai_na_notificacao_e_nunca_na_resposta()
    {
        var contas = new FakeUserAccounts();
        var notificador = new FakeNotifier();
        var convidar = new InviteUser(contas, new FakeAuditTrail(), notificador);

        var resultado = await convidar.ExecuteAsync(
            "novo@rivo.ao", "HR", LinkBase, Contexto, CancellationToken.None);

        // A resposta a quem convida traz o identificador da conta e mais nada.
        // Se trouxesse o testemunho, convidar bastava para entrar em nome de
        // outra pessoa — que é exactamente o que o convite existe para impedir.
        Assert.NotNull(resultado.UserId);

        var mensagem = Assert.Single(notificador.Queued).Message;
        Assert.Contains(LinkBase, mensagem);
        Assert.Contains("/convite?u=", mensagem);
    }

    [Fact]
    public async Task O_convite_pede_entrega_por_correio()
    {
        var contas = new FakeUserAccounts();
        var notificador = new FakeNotifier();
        var convidar = new InviteUser(contas, new FakeAuditTrail(), notificador);

        await convidar.ExecuteAsync(
            "novo@rivo.ao", "HR", LinkBase, Contexto, CancellationToken.None);

        // O teste que faltava, e a ausência custou um convite que chegou a
        // produção sem sair da aplicação. `SendEmail` tem por omissão
        // `false`, e uma notificação assim nasce `NotRequired`: fica na caixa
        // da aplicação, que é o único sítio onde quem foi convidado ainda não
        // consegue entrar.
        //
        // Verificar a mensagem não chegava: ela estava certa, e o correio não
        // saía à mesma.
        Assert.True(
            Assert.Single(notificador.Queued).SendEmail,
            "O convite tem de pedir entrega por correio: sem ela ninguém o recebe.");
    }

    [Fact]
    public async Task Perfil_desconhecido_e_recusado_antes_de_criar_a_conta()
    {
        var contas = new FakeUserAccounts();
        var convidar = new InviteUser(contas, new FakeAuditTrail(), new FakeNotifier());

        var resultado = await convidar.ExecuteAsync(
            "novo@rivo.ao", "NaoExiste", LinkBase, Contexto, CancellationToken.None);

        Assert.Equal(InviteUserOutcome.UnknownProfile, resultado.Outcome);

        // A ordem importa: recusar depois de criar deixaria uma conta órfã,
        // sem perfil e sem convite, que ninguém limpa.
        Assert.Empty(contas.Invited);
    }

    [Fact]
    public async Task O_SuperAdmin_nao_se_convida()
    {
        var contas = new FakeUserAccounts();
        var convidar = new InviteUser(contas, new FakeAuditTrail(), new FakeNotifier());

        // É a mesma propriedade do ADR-058 vista de outro ângulo: se o convite
        // aceitasse `SuperAdmin`, quem administra contas contornava por aqui a
        // recusa de o atribuir.
        var resultado = await convidar.ExecuteAsync(
            "intruso@rivo.ao", "SuperAdmin", LinkBase, Contexto, CancellationToken.None);

        Assert.Equal(InviteUserOutcome.UnknownProfile, resultado.Outcome);
        Assert.Empty(contas.Invited);
    }

    [Fact]
    public async Task Aceitar_com_o_testemunho_certo_fixa_a_password()
    {
        var contas = new FakeUserAccounts();
        var aceitar = new AcceptInvitation(contas, new FakeAuditTrail());

        var codificado = System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Encoding.UTF8.GetBytes(FakeUserAccounts.InvitationToken));

        var resultado = await aceitar.ExecuteAsync(
            Guid.NewGuid(), codificado, "Password!Forte123", Contexto, CancellationToken.None);

        Assert.Equal(PasswordChangeResult.Changed, resultado.Result);
    }

    [Fact]
    public async Task Testemunho_adulterado_e_recusado_sem_rebentar()
    {
        var contas = new FakeUserAccounts();
        var aceitar = new AcceptInvitation(contas, new FakeAuditTrail());

        // Base64Url inválido. Chega dos clientes de correio que partem
        // ligações em duas linhas, e não deve produzir 500.
        var resultado = await aceitar.ExecuteAsync(
            Guid.NewGuid(), "isto!nao@e*base64", "Password!Forte123", Contexto, CancellationToken.None);

        Assert.Equal(PasswordChangeResult.Rejected, resultado.Result);
    }
}
