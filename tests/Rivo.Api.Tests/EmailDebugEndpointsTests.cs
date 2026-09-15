using System.Reflection;
using Rivo.Api.Notifications;

namespace Rivo.Api.Tests;

/// <summary>
/// O endpoint de teste de correio não pode aceitar destinatário.
///
/// <para>
/// A primeira versão aceitava um endereço por query string, atrás de
/// `identity.users.write`. Isso fazia da API um relay: quem tivesse essa
/// permissão punha a caixa da organização a enviar correio, com o remetente do
/// domínio, para qualquer destino.
/// </para>
///
/// <para>
/// <strong>O risco já estava escrito num comentário da própria classe, e o
/// comentário não impediu nada.</strong> Daí este teste: a propriedade passa a
/// estar afirmada onde falha a construção, e não onde se lê a intenção.
/// </para>
/// </summary>
public sealed class EmailDebugEndpointsTests
{
    private static MethodInfo Handler =>
        typeof(EmailDebugEndpoints).GetMethod(
            "SendTestEmailAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "SendTestEmailAsync desapareceu ou mudou de nome — se o endpoint foi removido, " +
            "remova também este teste; se foi renomeado, actualize-o.");

    [Fact]
    public void Nao_aceita_destinatario_de_quem_chama()
    {
        var doCliente = Handler.GetParameters()
            .Where(p => p.ParameterType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        // Um `string` na assinatura de um handler de minimal API vem da query
        // string ou da rota — ou seja, de quem chama. O destino da mensagem tem
        // de sair da configuração (`Smtp:From`) e de mais lado nenhum.
        Assert.True(
            doCliente.Length == 0,
            "O endpoint de teste de correio voltou a aceitar entrada de texto de quem chama " +
            $"({string.Join(", ", doCliente)}). Se for um destinatário, isto é um relay aberto: " +
            "a mensagem tem de ir para `Smtp:From`. Para verificar entrega a uma pessoa concreta, " +
            "o caminho é o convite.");
    }
}
