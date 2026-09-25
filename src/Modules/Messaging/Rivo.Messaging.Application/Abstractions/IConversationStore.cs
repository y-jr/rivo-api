using Rivo.Messaging.Domain;
using Rivo.SharedKernel.Contracts;

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

    /// <summary>
    /// <paramref name="pagina"/> nulo devolve tudo, como antes de existir
    /// paginação (ADR-068) — o total só vem preenchido quando há página.
    /// </summary>
    Task<(IReadOnlyList<Conversation> Items, int? TotalCount)> ListByCustomerAsync(
        Guid customerId, ConversationKind? kind, PageRequest? pagina, CancellationToken cancellationToken);

    /// <summary>Mesma nota de paginação de <see cref="ListByCustomerAsync"/>.</summary>
    Task<(IReadOnlyList<Conversation> Items, int? TotalCount)> ListAsync(
        ConversationStatus? status, ConversationKind? kind, PageRequest? pagina, CancellationToken cancellationToken);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
