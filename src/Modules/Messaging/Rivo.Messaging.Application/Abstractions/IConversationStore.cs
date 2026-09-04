using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Application.Abstractions;

/// <summary>
/// Persistência de `messaging`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// A conversa aberta de um cliente de um dado <see cref="ConversationKind"/>,
    /// se houver — nunca mais do que uma **quando <paramref name="kind"/> é
    /// <see cref="ConversationKind.Message"/>** (a invariante que decide se
    /// uma mensagem nova entra na que já existe ou abre outra). Para
    /// <see cref="ConversationKind.Ticket"/> não é usado — vários tickets
    /// podem estar abertos ao mesmo tempo (ADR-046).
    /// </summary>
    Task<Conversation?> FindOpenByCustomerAsync(Guid customerId, ConversationKind kind, CancellationToken cancellationToken);

    /// <summary>Sem rastreio, com as mensagens: é leitura.</summary>
    Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Rastreada: quem a procura assim vai responder-lhe ou fechá-la.</summary>
    Task<Conversation?> FindForUpdateAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Conversation>> ListByCustomerAsync(
        Guid customerId, ConversationKind? kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<Conversation>> ListAsync(
        ConversationStatus? status, ConversationKind? kind, CancellationToken cancellationToken);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
