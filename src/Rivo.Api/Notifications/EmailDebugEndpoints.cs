using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MimeKit;
using Rivo.Identity.Contracts;
using Rivo.Notifications.Contracts;
using Rivo.Notifications.Domain;

namespace Rivo.Api.Notifications;

/// <summary>
/// Envia um e-mail de teste directamente pelo SMTP configurado, sem passar
/// por `notifications` nem por um utilizador real.
///
/// <para>
/// <strong>Existe só para verificar a ligação SMTP</strong> — confirmar
/// `Smtp:Host`/credenciais sem ter de convidar uma conta e ler-lhe o correio
/// (ADR-059). Não é o caminho de entrega da aplicação: esse é
/// <see cref="SmtpNotificationChannel"/>, accionado por eventos de negócio
/// através da fila de `notifications`.
/// </para>
///
/// <para>
/// <strong>Não recebe destinatário, e é essa a diferença que interessa.</strong>
/// A primeira versão aceitava um endereço por query string, protegido apenas
/// por `identity.users.write`. Isso fazia da API um relay: quem tivesse essa
/// permissão punha a caixa da organização a enviar correio, com o remetente do
/// domínio, para qualquer destino à escolha — o próprio comentário desta classe
/// nomeava o risco e deixava-o mitigado só pela permissão.
/// </para>
///
/// <para>
/// Agora a mensagem vai <strong>para a própria caixa configurada</strong>
/// (<c>Smtp:From</c>). Continua a provar tudo o que um teste de SMTP tem de
/// provar — ligação, TLS, autenticação e aceitação pelo servidor — e deixa de
/// poder alcançar terceiros. Para verificar entrega a uma pessoa concreta, o
/// caminho é o convite: é esse o percurso real, e é esse que interessa que
/// funcione.
/// </para>
///
/// <para>
/// O corpo passa pelo mesmo <see cref="CorpoDaMensagem"/> que desenha as
/// notificações verdadeiras, com botão e tudo. Assim o teste também responde à
/// pergunta seguinte — «e qual é o aspecto?» — sem ser preciso convidar alguém
/// para ver.
/// </para>
/// </summary>
public static class EmailDebugEndpoints
{
    public static IEndpointRouteBuilder MapEmailDebug(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/debug/email/test", SendTestEmailAsync)
            .RequireAuthorization(IdentityPermissions.UsersWrite);

        return endpoints;
    }

    private static async Task<IResult> SendTestEmailAsync(
        IOptions<SmtpOptions> options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var smtp = options.Value;

        // Vazio é estado válido (o mesmo que o CORS sem origens) — não faz
        // sentido a aplicação recusar arrancar por isso, mas este endpoint não
        // tem nada para fazer sem servidor configurado.
        if (!smtp.Enabled)
        {
            return Results.Problem(
                "Sem servidor de correio configurado (Smtp:Host/Smtp:From). Nada para testar.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        // `Enabled` garante que `From` está preenchido, mas não que é um
        // endereço. Um `Smtp:From` mal escrito falharia dentro do MailKit com
        // uma excepção que não nomeia a configuração que a causou.
        MailboxAddress caixa;
        try
        {
            caixa = MailboxAddress.Parse(smtp.From!);
        }
        catch (ParseException)
        {
            return Results.Problem(
                $"`Smtp:From` não é um endereço de e-mail válido: '{smtp.From}'.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(smtp.FromName, caixa.Address));
        mensagem.To.Add(caixa);
        mensagem.Subject = "Teste de correio — Rivo";
        mensagem.Body = CorpoDaMensagem.Construir(Amostra(configuration));

        await SmtpMailer.SendAsync(smtp, mensagem, cancellationToken);

        // Diz para onde foi. Sem isto, quem chama fica sem saber que caixa
        // abrir — e a resposta a essa pergunta é precisamente a configuração
        // que este endpoint existe para verificar.
        return Results.Ok(new { enviadoPara = caixa.Address });
    }

    /// <summary>
    /// Uma notificação de mentira, com a forma de um convite verdadeiro, para o
    /// teste exercitar o mesmo desenho que as mensagens reais usam.
    ///
    /// <para>
    /// O botão aponta para a página do convite com um testemunho inventado, e o
    /// texto diz isso — clicar dá erro, de propósito. Sem `Frontend:BaseUrl`
    /// configurado não há destino absoluto que sirva, e a amostra sai sem botão:
    /// é a mesma mensagem que uma notificação sem acção produziria.
    /// </para>
    /// </summary>
    private static Notification Amostra(IConfiguration configuration)
    {
        var frontend = configuration["Frontend:BaseUrl"];

        var destino = string.IsNullOrWhiteSpace(frontend)
            ? null
            : $"{frontend.TrimEnd('/')}/convite?u=00000000-0000-0000-0000-000000000000&t=amostra";

        var corpo =
            "Se está a ler isto, o servidor de correio do Rivo está configurado e a entregar.\n\n" +
            "Esta mensagem usa o mesmo desenho das notificações verdadeiras — é assim que " +
            "um convite chega a quem o recebe.\n\n" +
            (destino is null
                ? "Falta `Frontend:BaseUrl` na configuração, por isso a amostra sai sem botão. " +
                  "Um convite a sério também sairia assim, e a ligação não levaria a lado nenhum."
                : "O botão abaixo leva a um testemunho inventado e vai dar erro — é uma amostra, " +
                  "não um convite.");

        return Notification.Create(
            Guid.CreateVersion7(),
            NotificationTypes.UserInvited,
            "Teste de correio — Rivo",
            corpo,
            sendEmail: true,
            DateTimeOffset.UtcNow,
            destino,
            destino is null ? null : "Escolher a minha password");
    }
}
