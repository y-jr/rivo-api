using Rivo.Messaging.Application.UseCases;
using Rivo.Messaging.Contracts;

namespace Rivo.Messaging.Application;

/// <summary>
/// O contrato publicado de `messaging` para composição (ADR-045). Único
/// consumidor previsto: o Portal do Cliente.
/// </summary>
public sealed class CustomerMessaging(SendCustomerMessage send, ListMyConversations list) : ICustomerMessaging
{
    public Task<SendMessageResult> SendMessageAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken) =>
        send.ExecuteAsync(customerId, senderUserId, body, cancellationToken);

    public Task<IReadOnlyList<ConversationView>> ListMyConversationsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        list.ExecuteAsync(customerId, cancellationToken);
}
