using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Rivo.Identity.Contracts;
using Rivo.Notifications.Application;
using Rivo.Notifications.Domain;

namespace Rivo.Api.Notifications;

/// <summary>
/// Configuração do servidor de correio. Vazia significa não enviar — e é
/// estado válido, o mesmo que o CORS sem origens.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Vazio desliga o envio e deixa o canal de log a funcionar.</summary>
    public string? Host { get; init; }

    /// <summary>465 com SSL implícito, ou 587 com STARTTLS.</summary>
    public int Port { get; init; } = 465;

    public string? User { get; init; }

    public string? Password { get; init; }

    /// <summary>
    /// Remetente. Tem de ser uma caixa que exista no servidor: quase todos os
    /// fornecedores recusam enviar em nome de um endereço que não alojam, e a
    /// recusa chega como erro de autenticação, que engana.
    /// </summary>
    public string? From { get; init; }

    /// <summary>Nome por extenso do remetente, como aparece na caixa de quem recebe.</summary>
    public string FromName { get; init; } = "Rivo";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// Entrega notificações por correio electrónico.
///
/// <para>
/// <strong>Vive no host e não em `notifications`</strong>, e a razão é a
/// fronteira: uma notificação guarda <c>RecipientUserId</c> e mais nada, o
/// endereço vive em `identity`, e o módulo de notificações não depende de
/// módulo nenhum por desenho (verificado em <c>ProjectReferenceTests</c>).
/// Quem junta os dois é a composição — aqui — sem que nenhum dos módulos passe
/// a conhecer o outro.
/// </para>
///
/// <para>
/// Lançar excepção é o que sinaliza insucesso a quem chama, e faz o worker
/// agendar nova tentativa (<see cref="INotificationChannel"/>). Por isso os
/// erros de rede e de servidor sobem: são transitórios e merecem repetição.
/// A única recusa definitiva tratada aqui é não haver endereço para onde
/// enviar — repetir isso mil vezes não faz aparecer um.
/// </para>
/// </summary>
public sealed class SmtpNotificationChannel(
    IOptions<SmtpOptions> options,
    IUserDirectory users,
    ILogger<SmtpNotificationChannel> logger) : INotificationChannel
{
    private readonly SmtpOptions _options = options.Value;

    public async Task DeliverAsync(Notification notification, CancellationToken cancellationToken)
    {
        var destinatario = await users.FindAsync(notification.RecipientUserId, cancellationToken);

        if (destinatario is null)
        {
            // Conta apagada ou identificador que nunca existiu. Não é falha de
            // entrega: é uma notificação sem destino, e tentar de novo não a
            // arranja. Regista-se e dá-se por entregue.
            logger.LogWarning(
                "Notificação {NotificationId} sem destinatário resolúvel ({RecipientUserId}). Não enviada.",
                notification.Id, notification.RecipientUserId);
            return;
        }

        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(_options.FromName, _options.From));
        mensagem.To.Add(MailboxAddress.Parse(destinatario.Email));
        mensagem.Subject = notification.Title;
        mensagem.Body = new TextPart("plain") { Text = notification.Message };

        using var cliente = new SmtpClient();

        // 465 fala TLS desde o primeiro byte; 587 começa em claro e sobe com
        // STARTTLS. Escolher pelo porto evita uma opção a mais para enganar
        // quem configura.
        var seguranca = _options.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        await cliente.ConnectAsync(_options.Host, _options.Port, seguranca, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.User))
        {
            await cliente.AuthenticateAsync(_options.User, _options.Password, cancellationToken);
        }

        await cliente.SendAsync(mensagem, cancellationToken);
        await cliente.DisconnectAsync(quit: true, cancellationToken);

        // O assunto e o tipo bastam para confirmar a entrega; o corpo pode
        // levar um convite com token e não vai para os logs.
        logger.LogInformation(
            "Notificação {NotificationId} do tipo {Type} enviada por correio.",
            notification.Id, notification.Type);
    }
}
