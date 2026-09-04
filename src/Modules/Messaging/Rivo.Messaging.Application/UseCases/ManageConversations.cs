using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Domain;
using Rivo.Notifications.Contracts;

namespace Rivo.Messaging.Application.UseCases;

/// <summary>
/// Avisa o vendedor responsável de um cliente que este escreveu — o único
/// propósito de <c>Customer.AssignedToEmployeeId</c> (ADR-045 §2). Sem
/// vendedor atribuído, ninguém é avisado: a mensagem ou o ticket ficam só
/// na fila partilhada, visíveis a quem tiver permissão de ler conversas.
///
/// <para>
/// Partilhado entre <see cref="SendCustomerMessage"/> e os casos de uso de
/// tickets (ADR-046) — é o mesmo evento ("o cliente escreveu"), só muda o
/// título consoante <see cref="ConversationKind"/>.
/// </para>
/// </summary>
public sealed class NotifyAssignedOwner(ICustomerDirectory customers, IEmployeeDirectory employees, INotifier notifier)
{
    public async Task NotifyAsync(Conversation conversa, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindAsync(conversa.CustomerId, cancellationToken);

        if (cliente?.AssignedToEmployeeId is not { } vendedorId)
        {
            return;
        }

        var vendedor = await employees.FindAsync(vendedorId, DateTimeOffset.UtcNow, cancellationToken);

        if (vendedor?.UserId is not { } vendedorUserId)
        {
            return;
        }

        var (titulo, mensagem) = conversa.Kind switch
        {
            ConversationKind.Ticket => (
                "Novo ticket de suporte",
                $"{cliente.Name} abriu um ticket: {conversa.Subject}"),
            _ => (
                "Nova mensagem de cliente",
                $"{cliente.Name} enviou uma mensagem."),
        };

        await notifier.QueueAsync(
            new NotificationRequest(vendedorUserId, NotificationTypes.MessagingNewMessage, titulo, mensagem),
            cancellationToken);
    }
}

/// <summary>
/// O cliente escreve — entra na conversa aberta que já houver, ou abre uma
/// nova (ADR-045).
/// </summary>
public sealed class SendCustomerMessage(
    IConversationStore store,
    NotifyAssignedOwner notify,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken)
    {
        var aberta = await store.FindOpenByCustomerAsync(customerId, ConversationKind.Message, cancellationToken);
        var nova = aberta is null;
        var conversa = aberta ?? Conversation.OpenMessage(customerId, clock.GetUtcNow());

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

        await notify.NotifyAsync(conversa, cancellationToken);

        return SendMessageResult.Sent(conversa.Id, mensagem.Id);
    }
}

/// <summary>
/// O cliente abre um ticket de suporte, com assunto e a primeira mensagem
/// (ADR-046). Ao contrário de mensagens directas, várias podem estar
/// abertas ao mesmo tempo — cada uma cria sempre uma conversa nova.
/// </summary>
public sealed class OpenTicket(IConversationStore store, NotifyAssignedOwner notify, IAuditTrail audit, TimeProvider clock)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid customerId, Guid senderUserId, string subject, string body, CancellationToken cancellationToken)
    {
        Conversation conversa;

        try
        {
            conversa = Conversation.OpenTicket(customerId, subject, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return SendMessageResult.Rejected(error.Message);
        }

        Message mensagem;

        try
        {
            mensagem = conversa.AddMessage(MessageSender.Customer, senderUserId, body, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return SendMessageResult.Rejected(error.Message);
        }

        await store.AddAsync(conversa, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                MessagingAuditActions.TicketOpened,
                MessagingAuditEntityTypes.Conversation,
                conversa.Id.ToString(),
                new AuditContext(senderUserId, null, null),
                NewValue: $$"""{"subject":"{{conversa.Subject}}"}"""),
            cancellationToken);

        await notify.NotifyAsync(conversa, cancellationToken);

        return SendMessageResult.Sent(conversa.Id, mensagem.Id);
    }
}

/// <summary>
/// O cliente responde a **um** dos seus tickets (ADR-046) — ao contrário de
/// mensagens directas, aqui há vários possíveis, e o cliente escolhe qual.
/// A mesma resposta (não encontrado) serve "não existe", "não é teu" e "não
/// é um ticket": não se revela qual das três, mesma disciplina do
/// <c>PaymentClaim</c> (ADR-044).
/// </summary>
public sealed class AddCustomerTicketMessage(
    IConversationStore store, NotifyAssignedOwner notify, IAuditTrail audit, TimeProvider clock)
{
    public async Task<SendMessageResult> ExecuteAsync(
        Guid conversationId, Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken)
    {
        var conversa = await store.FindForUpdateAsync(conversationId, cancellationToken);

        if (conversa is null || conversa.CustomerId != customerId || conversa.Kind is not ConversationKind.Ticket)
        {
            return SendMessageResult.NotFound();
        }

        Message mensagem;

        try
        {
            mensagem = conversa.AddMessage(MessageSender.Customer, senderUserId, body, clock.GetUtcNow());
        }
        catch (ArgumentException error)
        {
            return SendMessageResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return SendMessageResult.Closed(error.Message);
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

        await notify.NotifyAsync(conversa, cancellationToken);

        return SendMessageResult.Sent(conversa.Id, mensagem.Id);
    }
}

/// <summary>
/// As conversas do próprio cliente, de um dado tipo — "as minhas mensagens"
/// ou "os meus tickets" do Portal do Cliente.
/// </summary>
public sealed class ListMyConversations(IConversationStore store)
{
    public async Task<IReadOnlyList<ConversationView>> ExecuteAsync(
        Guid customerId, ConversationKind kind, CancellationToken cancellationToken)
    {
        var conversas = await store.ListByCustomerAsync(customerId, kind, cancellationToken);

        return [.. conversas.OrderByDescending(c => c.OpenedAt).Select(ToView)];
    }

    internal static ConversationView ToView(Conversation conversa) =>
        new(
            conversa.Id,
            conversa.Kind.ToString(),
            conversa.Subject,
            conversa.Status.ToString(),
            conversa.OpenedAt,
            conversa.ClosedAt,
            [.. conversa.Messages
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageView(m.Id, m.Sender.ToString(), m.SenderUserId, m.Body, m.SentAt))]);
}

/// <summary>Sales responde. Nunca a uma conversa fechada — mesma regra do agregado. Serve mensagens e tickets, sem distinção.</summary>
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

/// <summary>Sales marca como resolvida. Serve mensagens e tickets, sem distinção.</summary>
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
/// permissão de ler (caixa partilhada, ADR-045 §2), com filtro opcional por
/// tipo (ADR-046) — mensagens directas ou tickets.
/// </summary>
public sealed class ListConversations(IConversationStore store, ICustomerDirectory customers)
{
    public async Task<IReadOnlyList<ConversationSummaryView>> ExecuteAsync(
        ConversationStatus? status, ConversationKind? kind, CancellationToken cancellationToken)
    {
        var conversas = await store.ListAsync(status, kind, cancellationToken);
        var vistas = new List<ConversationSummaryView>(conversas.Count);

        foreach (var conversa in conversas.OrderByDescending(c => c.OpenedAt))
        {
            var cliente = await customers.FindAsync(conversa.CustomerId, cancellationToken);

            vistas.Add(new ConversationSummaryView(
                conversa.Id,
                conversa.CustomerId,
                cliente?.Name ?? "(cliente desconhecido)",
                cliente?.AssignedToEmployeeId,
                conversa.Kind.ToString(),
                conversa.Subject,
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
    string Kind,
    string? Subject,
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

    /// <summary>Abertura de ticket (ADR-046) — acção própria, distinta de <see cref="MessageSent"/>: o assunto só existe neste momento.</summary>
    public const string TicketOpened = "messaging.ticket.opened";
}

public static class MessagingAuditEntityTypes
{
    public const string Conversation = "messaging.conversation";
}
