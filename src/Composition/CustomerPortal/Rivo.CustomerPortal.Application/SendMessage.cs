using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// O cliente escreve à equipa comercial — quarto caso de uso do Portal do
/// Cliente (ADR-045), mesma resolução de "o próprio" de
/// <see cref="GetMyOverview"/>. Delega tudo o resto a `messaging` através
/// de <see cref="ICustomerMessaging"/>.
/// </summary>
public sealed class SendMessage(ICustomerDirectory customers, ICustomerMessaging messaging)
{
    public async Task<SendMessageResult> ExecuteAsync(Guid userId, string body, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return SendMessageResult.NotLinked();
        }

        var resultado = await messaging.SendMessageAsync(cliente.CustomerId, userId, body, cancellationToken);

        return SendMessageResult.From(resultado);
    }
}

public enum SendMessageOutcome
{
    Sent,
    NotLinked,
    Rejected,
}

public sealed record SendMessageResult(SendMessageOutcome Outcome, Guid? ConversationId, Guid? MessageId, string? Error)
{
    public static SendMessageResult NotLinked() => new(SendMessageOutcome.NotLinked, null, null, null);

    public static SendMessageResult From(Rivo.Messaging.Contracts.SendMessageResult inner) => new(
        inner.Outcome switch
        {
            Rivo.Messaging.Contracts.SendMessageOutcome.Sent => SendMessageOutcome.Sent,
            _ => SendMessageOutcome.Rejected,
        },
        inner.ConversationId,
        inner.MessageId,
        inner.Error);
}
