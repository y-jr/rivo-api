using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Domain;
using Rivo.Notifications.Contracts;

namespace Rivo.Messaging.Application.UseCases;

/// <summary>
/// O cliente escreve — entra na conversa aberta que já houver, ou abre uma
/// nova (ADR-045). Avisa o vendedor responsável, se houver um atribuído;
/// sem vendedor, a mensagem fica na fila partilhada sem aviso a ninguém.
/// </summary>
public sealed class SendCustomerMessage(
    IConversationStore store,
    ICustomerDirectory customers,
    IEmployeeDirectory employees,
    INotifier notifier,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken)
    {
        var aberta = await store.FindOpenByCustomerAsync(customerId, cancellationToken);
        var nova = aberta is null;
        var conversa = aberta ?? Conversation.Open(customerId, clock.GetUtcNow());

        Message mensagem;

        try
        {
            mensagem = conversa.AddMessage(MessageSender.Customer, senderUserId, body, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return SendMessageResult.Rejected(error.Message);
        }

        if (nova)
        {
            await store.AddAsync(conversa, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                MessagingAuditActions.MessageSent,
                MessagingAuditEntityTypes.Conversation,
                conversa.Id.ToString(),
                new AuditContext(senderUserId, null, null),
                NewValue: $$"""{"sender":"Customer","conversationId":"{{conversa.Id}}"}"""),
            cancellationToken);

        // Avisar o vendedor responsável — o único propósito da atribuição
        // (ADR-045 §2). Sem ele, ninguém a avisar: a mensagem fica na fila
        // partilhada, visível a quem tiver permissão de ler conversas.
        var cliente = await customers.FindAsync(customerId, cancellationToken);

        if (cliente?.AssignedToEmployeeId is { } vendedorId)
        {
            var vendedor = await employees.FindAsync(vendedorId, clock.GetUtcNow(), cancellationToken);

            if (vendedor?.UserId is { } vendedorUserId)
            {
                await notifier.QueueAsync(
                    new NotificationRequest(
                        vendedorUserId,
                        NotificationTypes.MessagingNewMessage,
                        "Nova mensagem de cliente",
                        $"{cliente.Name} enviou uma mensagem."),
                    cancellationToken);
            }
        }

        return SendMessageResult.Sent(conversa.Id, mensagem.Id);
    }
}

/// <summary>As conversas do próprio cliente — "as minhas mensagens" do Portal do Cliente.</summary>
public sealed class ListMyConversations(IConversationStore store)
{
    public async Task<IReadOnlyList<ConversationView>> ExecuteAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var conversas = await store.ListByCustomerAsync(customerId, cancellationToken);

        return [.. conversas.OrderByDescending(c => c.OpenedAt).Select(ToView)];
    }

    internal static ConversationView ToView(Conversation conversa) =>
        new(
            conversa.Id,
            conversa.Status.ToString(),
            conversa.OpenedAt,
            conversa.ClosedAt,
            [.. conversa.Messages
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageView(m.Id, m.Sender.ToString(), m.SenderUserId, m.Body, m.SentAt))]);
}

/// <summary>Sales responde. Nunca a uma conversa fechada — mesma regra do agregado.</summary>
public sealed class SendEmployeeReply(IConversationStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<ReplyResult> ExecuteAsync(
        Guid conversationId, Guid senderUserId, string body, AuditContext context, CancellationToken cancellationToken)
    {
        var conversa = await store.FindForUpdateAsync(conversationId, cancellationToken);

        if (conversa is null)
        {
            return ReplyResult.NotFound();
        }

        Message mensagem;

        try
        {
            mensagem = conversa.AddMessage(MessageSender.Employee, senderUserId, body, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return ReplyResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return ReplyResult.Closed(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                MessagingAuditActions.MessageSent,
                MessagingAuditEntityTypes.Conversation,
                conversa.Id.ToString(),
                context,
                NewValue: $$"""{"sender":"Employee","conversationId":"{{conversa.Id}}"}"""),
            cancellationToken);

        return ReplyResult.Sent(mensagem.Id);
    }
}

public sealed record ReplyResult(ReplyOutcome Outcome, Guid? MessageId, string? Error)
{
    public static ReplyResult Sent(Guid messageId) => new(ReplyOutcome.Sent, messageId, null);

    public static ReplyResult NotFound() => new(ReplyOutcome.NotFound, null, "Conversa não encontrada.");

    /// <summary>Corpo vazio ou longo demais — 400.</summary>
    public static ReplyResult Rejected(string error) => new(ReplyOutcome.Rejected, null, error);

    /// <summary>Conversa fechada — 409, não pedido mal formado.</summary>
    public static ReplyResult Closed(string error) => new(ReplyOutcome.Closed, null, error);
}

public enum ReplyOutcome
{
    Sent,
    NotFound,
    Rejected,
    Closed,
}

/// <summary>Sales marca como resolvida.</summary>
public sealed class CloseConversation(IConversationStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CloseConversationOutcome> ExecuteAsync(
        Guid conversationId, Guid closedByUserId, AuditContext context, CancellationToken cancellationToken)
    {
        var conversa = await store.FindForUpdateAsync(conversationId, cancellationToken);

        if (conversa is null)
        {
            return CloseConversationOutcome.NotFound;
        }

        try
        {
            conversa.Close(closedByUserId, clock.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return CloseConversationOutcome.AlreadyClosed;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                MessagingAuditActions.ConversationClosed,
                MessagingAuditEntityTypes.Conversation,
                conversa.Id.ToString(),
                context),
            cancellationToken);

        return CloseConversationOutcome.Closed;
    }
}

public enum CloseConversationOutcome
{
    Closed,
    NotFound,

    /// <summary>Já estava fechada — 409.</summary>
    AlreadyClosed,
}

/// <summary>
/// A fila de `messaging` — todas as conversas, visíveis a quem tiver
/// permissão de ler (caixa partilhada, ADR-045 §2).
/// </summary>
public sealed class ListConversations(IConversationStore store, ICustomerDirectory customers)
{
    public async Task<IReadOnlyList<ConversationSummaryView>> ExecuteAsync(
        ConversationStatus? status, CancellationToken cancellationToken)
    {
        var conversas = await store.ListAsync(status, cancellationToken);
        var vistas = new List<ConversationSummaryView>(conversas.Count);

        foreach (var conversa in conversas.OrderByDescending(c => c.OpenedAt))
        {
            var cliente = await customers.FindAsync(conversa.CustomerId, cancellationToken);

            vistas.Add(new ConversationSummaryView(
                conversa.Id,
                conversa.CustomerId,
                cliente?.Name ?? "(cliente desconhecido)",
                cliente?.AssignedToEmployeeId,
                conversa.Status.ToString(),
                conversa.OpenedAt,
                conversa.Messages.Count));
        }

        return vistas;
    }
}

public sealed record ConversationSummaryView(
    Guid ConversationId,
    Guid CustomerId,
    string CustomerName,
    Guid? AssignedToEmployeeId,
    string Status,
    DateTimeOffset OpenedAt,
    int MessageCount);

/// <summary>Uma conversa com todas as mensagens — o ecrã de resposta de Sales.</summary>
public sealed class GetConversation(IConversationStore store)
{
    public async Task<ConversationView?> ExecuteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversa = await store.FindAsync(conversationId, cancellationToken);

        return conversa is null ? null : ListMyConversations.ToView(conversa);
    }
}

public static class MessagingAuditActions
{
    public const string MessageSent = "messaging.message.sent";
    public const string ConversationClosed = "messaging.conversation.closed";
}

public static class MessagingAuditEntityTypes
{
    public const string Conversation = "messaging.conversation";
}
