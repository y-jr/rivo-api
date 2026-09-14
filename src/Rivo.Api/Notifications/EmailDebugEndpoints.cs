using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MimeKit;
using Rivo.Identity.Contracts;

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
/// <strong>Atrás de `identity.users.write`</strong>, a mesma permissão que já
/// decide quem convida contas — porque um relay de e-mail aberto na API
/// publicada seria uma forma de mandar correio arbitrário através da caixa da
/// organização. ⚠ Vale a pena remover ou desligar este endpoint antes de
/// depender dele em produção a sério; ficou deliberadamente sem interruptor
/// próprio (like `EXPOSE_OPENAPI`, ADR-038) por não ter sido pedido.
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
        string to,
        IOptions<SmtpOptions> options,
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

        var mensagem = new MimeMessage();

        // `smtp.From` é anulável no tipo (nada o obrigava antes de existir
        // consumidor nenhum), mas `Enabled` já confirmou que está preenchido —
        // daí o `!` em vez de mais uma verificação que nunca dispara.
        mensagem.From.Add(new MailboxAddress(smtp.FromName, smtp.From!));
        mensagem.Subject = "Teste SMTP - Rivo";
        mensagem.Body = new BodyBuilder
        {
            HtmlBody = "<h1>SMTP funcionando</h1><p>Este e-mail foi enviado pela API Rivo.</p>",
        }.ToMessageBody();

        try
        {
            mensagem.To.Add(MailboxAddress.Parse(to));
        }
        catch (ParseException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = [$"'{to}' não é um endereço de e-mail válido."],
            });
        }

        await SmtpMailer.SendAsync(smtp, mensagem, cancellationToken);

        return Results.Ok();
    }
}
