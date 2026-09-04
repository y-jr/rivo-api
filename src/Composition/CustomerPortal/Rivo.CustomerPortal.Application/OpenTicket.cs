using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// O cliente abre um ticket de suporte (ADR-046), mesma resolução de "o
/// próprio" de <see cref="GetMyOverview"/>. Delega tudo o resto a
/// `messaging` através de <see cref="ICustomerMessaging"/>.
/// </summary>
public sealed class OpenTicket(ICustomerDirectory customers, ICustomerMessaging messaging)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid userId, string subject, string body, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return SendMessageResult.NotLinked();
        }

        var resultado = await messaging.OpenTicketAsync(cliente.CustomerId, userId, subject, body, cancellationToken);

        return SendMessageResult.From(resultado);
    }
}
