using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>As conversas do próprio cliente com a equipa comercial (ADR-045).</summary>
public sealed class ListMyMessages(ICustomerDirectory customers, ICustomerMessaging messaging)
{
    public async Task<ListMyMessagesResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return ListMyMessagesResult.NotLinked();
        }

        var conversas = await messaging.ListMyConversationsAsync(cliente.CustomerId, cancellationToken);

        return ListMyMessagesResult.Found(conversas);
    }
}

public enum ListMyMessagesOutcome
{
    Found,
    NotLinked,
}

public sealed record ListMyMessagesResult(ListMyMessagesOutcome Outcome, IReadOnlyList<ConversationView>? Conversations)
{
    public static ListMyMessagesResult Found(IReadOnlyList<ConversationView> conversations) =>
        new(ListMyMessagesOutcome.Found, conversations);

    public static ListMyMessagesResult NotLinked() => new(ListMyMessagesOutcome.NotLinked, null);
}
