using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.UseCases;
using Rivo.Notifications.Contracts;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// A recuperação de password pelo próprio (ADR-065).
///
/// <para>
/// <strong>A propriedade que estes testes protegem é uma indiferença:</strong>
/// pedir a recuperação de um endereço com conta e de um endereço inventado tem
/// de ser indistinguível de fora. O que muda é o que acontece <em>depois</em> —
/// sai correio num caso e não no outro — e isso verifica-se aqui, onde se vê o
/// que foi enfileirado, e não numa resposta HTTP que é igual nos dois.
/// </para>
/// </summary>
public class RecoverPasswordTests
{
    private const string LinkBase = "https://rivo.exemplo.ao";
    private const string Email = "ana.silva@rivo.ao";

    private static AuditContext Contexto() =>
        new(null, "192.0.2.10", Guid.NewGuid().ToString());

    private static (RecoverPassword Caso, FakeUserAccounts Contas, FakeNotifier Correio, FakeAuditTrail Trilha)
        Montar(bool comConta)
    {
        var contas = new FakeUserAccounts();
        var correio = new FakeNotifier();
        var trilha = new FakeAuditTrail();

        if (comConta)
        {
            contas.ComConta.Add(Email);
        }

        return (new RecoverPassword(contas, trilha, correio), contas, correio, trilha);
    }

    [Fact]
    public async Task ComContaActiva_EnviaOTestemunhoParaOEndereco()
    {
        var (caso, contas, correio, _) = Montar(comConta: true);

        await caso.ExecuteAsync(Email, LinkBase, Contexto(), CancellationToken.None);

        var pedido = Assert.Single(correio.Queued);
        Assert.Equal(NotificationTypes.PasswordRecovery, pedido.Type);
        Assert.Equal(contas.ContaDaRecuperacao, pedido.RecipientUserId);

        // Sem entrega externa esta notificação não serve para nada — quem perdeu
        // a password não entra na aplicação para a ler lá dentro.
        Assert.True(pedido.SendEmail);

        Assert.StartsWith($"{LinkBase}/recuperar?u={contas.ContaDaRecuperacao}&t=", pedido.ActionUrl);
        Assert.Equal("Escolher uma password nova", pedido.ActionLabel);
    }

    /// <summary>
    /// O testemunho vai no endereço e não no corpo da mensagem: o canal de
    /// correio desenha-o como botão, e um URL cru no texto convida a copiá-lo
    /// para outro sítio.
    /// </summary>
    [Fact]
    public async Task OTestemunhoNaoApareceNoTextoDaMensagem()
    {
        var (caso, _, correio, _) = Montar(comConta: true);

        await caso.ExecuteAsync(Email, LinkBase, Contexto(), CancellationToken.None);

        var pedido = Assert.Single(correio.Queued);
        Assert.DoesNotContain(FakeUserAccounts.RecoveryToken, pedido.Message);
        Assert.DoesNotContain("http", pedido.Message);
    }

    [Fact]
    public async Task SemConta_NaoEnviaNadaENaoLevantaNada()
    {
        var (caso, _, correio, _) = Montar(comConta: false);

        await caso.ExecuteAsync("quem.nao.existe@exemplo.ao", LinkBase, Contexto(), CancellationToken.None);

        // A ausência de correio é a única diferença observável — e não é
        // observável de fora, que é o ponto.
        Assert.Empty(correio.Queued);
    }

    [Fact]
    public async Task SemConta_FicaNaTrilhaComOEnderecoTentado()
    {
        var (caso, _, _, trilha) = Montar(comConta: false);

        await caso.ExecuteAsync("quem.nao.existe@exemplo.ao", LinkBase, Contexto(), CancellationToken.None);

        var registo = Assert.Single(trilha.Records);
        Assert.Equal("identity.user.password_recovery_requested", registo.Action);

        // Sem conta não há identificador: a entidade é o endereço tentado. É a
        // única pista que sobra de uma rota que responde sempre o mesmo, e uma
        // sequência destes é o que se quer poder ver depois.
        Assert.Equal("quem.nao.existe@exemplo.ao", registo.EntityId);
        Assert.Contains("\"found\":false", registo.NewValue);
    }

    [Fact]
    public async Task ComConta_FicaNaTrilhaComOIdentificador()
    {
        var (caso, contas, _, trilha) = Montar(comConta: true);

        await caso.ExecuteAsync(Email, LinkBase, Contexto(), CancellationToken.None);

        var registo = Assert.Single(trilha.Records);
        Assert.Equal(contas.ContaDaRecuperacao.ToString(), registo.EntityId);
        Assert.Contains("\"found\":true", registo.NewValue);
    }

    [Fact]
    public async Task OEnderecoEAparadoAntesDeProcurar()
    {
        var (caso, contas, correio, _) = Montar(comConta: true);

        await caso.ExecuteAsync($"  {Email}  ", LinkBase, Contexto(), CancellationToken.None);

        // Um espaço a mais, colado de um formulário, não devia significar «esta
        // conta não existe» — e sem correio ninguém percebia porquê.
        Assert.Single(correio.Queued);
        Assert.Equal($"  {Email}  ", Assert.Single(contas.RecuperacoesPedidas));
    }

    [Fact]
    public async Task ABarraFinalDoLinkBaseNaoDuplica()
    {
        var (caso, _, correio, _) = Montar(comConta: true);

        await caso.ExecuteAsync(Email, $"{LinkBase}/", Contexto(), CancellationToken.None);

        Assert.DoesNotContain("//recuperar", Assert.Single(correio.Queued).ActionUrl);
    }
}

/// <summary>
/// A conclusão da recuperação — consumir o testemunho.
/// </summary>
public class CompletePasswordRecoveryTests
{
    private static AuditContext Contexto() =>
        new(null, "192.0.2.10", Guid.NewGuid().ToString());

    private static string Codificar(string token) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(token));

    [Fact]
    public async Task ComTestemunhoValido_MudaAPasswordEAudita()
    {
        var contas = new FakeUserAccounts();
        var trilha = new FakeAuditTrail();

        var resultado = await new CompletePasswordRecovery(contas, trilha).ExecuteAsync(
            Guid.NewGuid(),
            Codificar(FakeUserAccounts.RecoveryToken),
            "Rivo!Password2026",
            Contexto(),
            CancellationToken.None);

        Assert.Equal(PasswordChangeResult.Changed, resultado.Result);
        Assert.Equal("identity.user.password_recovery_completed", Assert.Single(trilha.Records).Action);
    }

    [Fact]
    public async Task ComTestemunhoErrado_RecusaEAuditaAFalha()
    {
        var trilha = new FakeAuditTrail();

        var resultado = await new CompletePasswordRecovery(new FakeUserAccounts(), trilha).ExecuteAsync(
            Guid.NewGuid(),
            Codificar("outro-testemunho"),
            "Rivo!Password2026",
            Contexto(),
            CancellationToken.None);

        Assert.Equal(PasswordChangeResult.Rejected, resultado.Result);

        // Uma tentativa recusada é o rasto de uma ligação já usada, expirada, ou
        // de alguém a adivinhar — e é isso que se quer ver na trilha.
        Assert.Equal("identity.user.password_recovery_failed", Assert.Single(trilha.Records).Action);
    }

    [Theory]
    [InlineData("nao-e-base64-url!!")]
    [InlineData("")]
    public async Task ComTestemunhoMalformado_DizOMesmoQueExpirado(string token)
    {
        var trilha = new FakeAuditTrail();

        var resultado = await new CompletePasswordRecovery(new FakeUserAccounts(), trilha).ExecuteAsync(
            Guid.NewGuid(), token, "Rivo!Password2026", Contexto(), CancellationToken.None);

        Assert.Equal(PasswordChangeResult.Rejected, resultado.Result);

        // A mensagem não distingue malformado de expirado: distinguir ensinava a
        // separar uma tentativa inválida de uma que chegou tarde.
        Assert.Contains("inválida ou expirada", Assert.Single(resultado.Errors));
    }
}
