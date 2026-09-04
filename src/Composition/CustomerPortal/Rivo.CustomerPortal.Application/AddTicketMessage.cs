using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// O cliente responde a um dos seus tickets (ADR-046), mesma resolução de
/// "o próprio" de <see cref="GetMyOverview"/>. Delega tudo o resto a
/// `messaging` através de <see cref="ICustomerMessaging"/> — incluindo a
/// verificação de que o ticket é mesmo do cliente.
/// </summary>
public sealed class AddTicketMessage(ICustomerDirectory customers, ICustomerMessaging messaging)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid userId, Guid conversationId, string body, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return SendMessageResult.NotLinked();
        }

        var resultado = await messaging.AddTicketMessageAsync(
            cliente.CustomerId, conversationId, userId, body, cancellationToken);

        return SendMessageResult.From(resultado);
    }
}
