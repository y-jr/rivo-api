using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>Os tickets de suporte do próprio cliente (ADR-046).</summary>
public sealed class ListMyTickets(ICustomerDirectory customers, ICustomerMessaging messaging)
{
    public async Task<ListMyMessagesResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return ListMyMessagesResult.NotLinked();
        }

        var tickets = await messaging.ListMyTicketsAsync(cliente.CustomerId, cancellationToken);

        return ListMyMessagesResult.Found(tickets);
    }
}
