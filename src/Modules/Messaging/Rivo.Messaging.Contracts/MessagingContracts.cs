namespace Rivo.Messaging.Contracts;

/// <summary>
/// Superfície publicada de `messaging` para o cliente enviar mensagens
/// (ADR-045). Assembly sem dependências (ADR-017). Único consumidor
/// previsto: o Portal do Cliente.
///
/// <para>
/// A composição resolve "o próprio cliente" e delega tudo o resto — mesmo
/// padrão de <c>ICustomerPayments</c> (ADR-044): `messaging` valida
/// sozinho o que precisar de validar, sem repetir nada que a composição já
/// teria de repetir.
/// </para>
/// </summary>
public interface ICustomerMessaging
{
    /// <param name="customerId">Resolvido pela composição a partir de `CurrentUser` — nunca vem do pedido do cliente.</param>
    /// <param name="senderUserId">A conta que escreveu, para o rasto de auditoria e para <see cref="MessageView.SenderUserId"/>.</param>
    Task<SendMessageResult> SendMessageAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken);

    /// <summary>As conversas do próprio cliente, cada uma com as suas mensagens — "as minhas mensagens" do Portal do Cliente.</summary>
    Task<IReadOnlyList<ConversationView>> ListMyConversationsAsync(Guid customerId, CancellationToken cancellationToken);
}

public sealed record SendMessageResult(SendMessageOutcome Outcome, Guid? ConversationId, Guid? MessageId, string? Error)
{
    public static SendMessageResult Sent(Guid conversationId, Guid messageId) =>
        new(SendMessageOutcome.Sent, conversationId, messageId, null);

    public static SendMessageResult Rejected(string error) =>
        new(SendMessageOutcome.Rejected, null, null, error);
}

public enum SendMessageOutcome
{
    Sent,

    /// <summary>Corpo vazio, ou maior do que o limite — 400.</summary>
    Rejected,
}

public sealed record ConversationView(
    Guid ConversationId,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<MessageView> Messages);

/// <param name="Sender"><c>"Customer"</c> ou <c>"Employee"</c>.</param>
public sealed record MessageView(Guid MessageId, string Sender, Guid SenderUserId, string Body, DateTimeOffset SentAt);

/// <summary>Catálogo de permissões de `messaging`, declarado pelo próprio módulo.</summary>
public static class MessagingPermissions
{
    /// <summary>Ver conversas — a fila partilhada de Sales.</summary>
    public const string ConversationsRead = "messaging.conversations.read";

    /// <summary>
    /// Responder e fechar conversas. Sem audiência restrita ao vendedor
    /// responsável — ADR-045 §2 é explícito: a atribuição só decide quem é
    /// notificado, não quem pode agir.
    /// </summary>
    public const string ConversationsWrite = "messaging.conversations.write";

    public static readonly IReadOnlyList<string> All = [ConversationsRead, ConversationsWrite];
}
