namespace Rivo.Messaging.Contracts;

/// <summary>
/// Superfície publicada de `messaging` para o cliente enviar mensagens
/// (ADR-045) e abrir/acompanhar tickets de suporte (ADR-046). Assembly sem
/// dependências (ADR-017). Único consumidor previsto: o Portal do Cliente.
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

    /// <summary>
    /// Abre um ticket de suporte, com assunto e a primeira mensagem
    /// (ADR-046). Ao contrário de <see cref="SendMessageAsync"/>, cria
    /// sempre uma conversa nova — vários tickets podem estar abertos ao
    /// mesmo tempo.
    /// </summary>
    Task<SendMessageResult> OpenTicketAsync(
        Guid customerId, Guid senderUserId, string subject, string body, CancellationToken cancellationToken);

    /// <param name="conversationId">
    /// O ticket a que se responde — ao contrário de <see cref="SendMessageAsync"/>,
    /// aqui há vários possíveis, e o cliente escolhe qual.
    /// </param>
    Task<SendMessageResult> AddTicketMessageAsync(
        Guid customerId, Guid conversationId, Guid senderUserId, string body, CancellationToken cancellationToken);

    /// <summary>Os tickets do próprio cliente — "os meus tickets" do Portal do Cliente.</summary>
    Task<IReadOnlyList<ConversationView>> ListMyTicketsAsync(Guid customerId, CancellationToken cancellationToken);
}

public sealed record SendMessageResult(SendMessageOutcome Outcome, Guid? ConversationId, Guid? MessageId, string? Error)
{
    public static SendMessageResult Sent(Guid conversationId, Guid messageId) =>
        new(SendMessageOutcome.Sent, conversationId, messageId, null);

    public static SendMessageResult Rejected(string error) =>
        new(SendMessageOutcome.Rejected, null, null, error);

    /// <summary>
    /// Não encontrado — serve "não existe", "não é teu" e "não é um ticket"
    /// de uma vez, sem revelar qual (ADR-046, mesma disciplina do
    /// <c>PaymentClaim</c>, ADR-044). Só relevante em
    /// <see cref="ICustomerMessaging.AddTicketMessageAsync"/>.
    /// </summary>
    public static SendMessageResult NotFound() =>
        new(SendMessageOutcome.NotFound, null, null, "Ticket não encontrado.");

    /// <summary>Ticket já fechado — 409, não pedido mal formado. Só relevante em <see cref="ICustomerMessaging.AddTicketMessageAsync"/>.</summary>
    public static SendMessageResult Closed(string error) =>
        new(SendMessageOutcome.Closed, null, null, error);
}

public enum SendMessageOutcome
{
    Sent,

    /// <summary>Corpo ou assunto vazio, ou maior do que o limite — 400.</summary>
    Rejected,

    /// <summary>Só em <see cref="ICustomerMessaging.AddTicketMessageAsync"/> — 404.</summary>
    NotFound,

    /// <summary>Só em <see cref="ICustomerMessaging.AddTicketMessageAsync"/> — 409.</summary>
    Closed,
}

/// <param name="Kind"><c>"Message"</c> ou <c>"Ticket"</c> (ADR-046).</param>
/// <param name="Subject">Só em tickets — nulo em mensagens directas.</param>
public sealed record ConversationView(
    Guid ConversationId,
    string Kind,
    string? Subject,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<MessageView> Messages);

/// <param name="Sender"><c>"Customer"</c> ou <c>"Employee"</c>.</param>
public sealed record MessageView(Guid MessageId, string Sender, Guid SenderUserId, string Body, DateTimeOffset SentAt);

/// <summary>Catálogo de permissões de `messaging`, declarado pelo próprio módulo.</summary>
public static class MessagingPermissions
{
    /// <summary>Ver conversas — a fila partilhada de Sales. Cobre mensagens directas e tickets (ADR-046).</summary>
    public const string ConversationsRead = "messaging.conversations.read";

    /// <summary>
    /// Responder e fechar conversas. Sem audiência restrita ao vendedor
    /// responsável — ADR-045 §2 é explícito: a atribuição só decide quem é
    /// notificado, não quem pode agir.
    /// </summary>
    public const string ConversationsWrite = "messaging.conversations.write";

    public static readonly IReadOnlyList<string> All = [ConversationsRead, ConversationsWrite];
}
