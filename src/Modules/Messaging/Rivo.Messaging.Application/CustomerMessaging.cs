using Rivo.Messaging.Application.UseCases;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Application;

/// <summary>
/// O contrato publicado de `messaging` para composição (ADR-045, ADR-046).
/// Único consumidor previsto: o Portal do Cliente.
/// </summary>
public sealed class CustomerMessaging(
    SendCustomerMessage send,
    OpenTicket openTicket,
    AddCustomerTicketMessage addTicketMessage,
    ListMyConversations list) : ICustomerMessaging
{
    public Task<SendMessageResult> SendMessageAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken) =>
        send.ExecuteAsync(customerId, senderUserId, body, cancellationToken);

    public Task<IReadOnlyList<ConversationView>> ListMyConversationsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        list.ExecuteAsync(customerId, ConversationKind.Message, cancellationToken);

    public Task<SendMessageResult> OpenTicketAsync(
        Guid customerId, Guid senderUserId, string subject, string body, CancellationToken cancellationToken) =>
        openTicket.ExecuteAsync(customerId, senderUserId, subject, body, cancellationToken);

    public Task<SendMessageResult> AddTicketMessageAsync(
        Guid customerId, Guid conversationId, Guid senderUserId, string body, CancellationToken cancellationToken) =>
        addTicketMessage.ExecuteAsync(conversationId, customerId, senderUserId, body, cancellationToken);

    public Task<IReadOnlyList<ConversationView>> ListMyTicketsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        list.ExecuteAsync(customerId, ConversationKind.Ticket, cancellationToken);
}
