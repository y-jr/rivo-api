using MimeKit;
using Rivo.Api.Notifications;
using Rivo.Finance.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga <see cref="IFiscalDocumentDelivery"/> — declarado por `finance` — ao
/// servidor de correio.
///
/// <para>
/// Reutiliza <c>SmtpMailer</c>, o mesmo que entrega notificações e o teste de
/// SMTP, para que a escolha de porta e de segurança exista num sítio só.
/// </para>
///
/// <para>
/// <strong>Devolve o erro em vez de o lançar</strong>, ao contrário do canal de
/// notificações. A diferença é quem está do outro lado: uma notificação é
/// entregue por um worker que repete mais tarde, e aí a excepção é o sinal certo;
/// isto é uma pessoa à espera de resposta a um clique, e ela precisa de ler o que
/// correu mal para poder corrigir o endereço.
/// </para>
/// </summary>
public sealed class SmtpFiscalDocumentDelivery(
    SmtpOptions options,
    ILogger<SmtpFiscalDocumentDelivery> logger) : IFiscalDocumentDelivery
{
    public async Task<FiscalDocumentDeliveryResult> SendAsync(
        FiscalDocumentDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (!options.Enabled)
        {
            // Configuração em falta, não avaria. A mensagem diz o que preencher
            // em vez de mandar quem clicou procurar num log.
            return FiscalDocumentDeliveryResult.Failed(
                "O envio de correio não está configurado neste ambiente (Smtp:Host e Smtp:From).");
        }

        try
        {
            var mensagem = new MimeMessage();

            mensagem.From.Add(new MailboxAddress(options.FromName, options.From));
            mensagem.To.Add(new MailboxAddress(delivery.ToName, delivery.ToAddress));
            mensagem.Subject = delivery.Subject;

            var corpo = new BodyBuilder { TextBody = delivery.Body };

            corpo.Attachments.Add(
                delivery.FileName,
                delivery.Content,
                new ContentType("application", "pdf"));

            mensagem.Body = corpo.ToMessageBody();

            await SmtpMailer.SendAsync(options, mensagem, cancellationToken);

            return FiscalDocumentDeliveryResult.Ok();
        }
        catch (OperationCanceledException)
        {
            // Cancelamento não é falha de entrega — sobe.
            throw;
        }
        catch (Exception excepcao)
        {
            // O log leva o detalhe técnico; a resposta leva a frase que ajuda
            // quem clicou. O endereço de destino não vai para o log: já está na
            // trilha de auditoria, que é onde pertence.
            logger.LogError(excepcao, "Falha ao entregar documento fiscal por correio.");

            return FiscalDocumentDeliveryResult.Failed(excepcao.Message);
        }
    }
}
