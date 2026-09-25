using Microsoft.EntityFrameworkCore;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Messaging.Infrastructure.Persistence;

public sealed class ConversationStore(MessagingDbContext context) : IConversationStore
{
    public async Task<Conversation?> FindOpenByCustomerAsync(
        Guid customerId, ConversationKind kind, CancellationToken cancellationToken) =>
        await context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(
                c => c.CustomerId == customerId && c.Kind == kind && c.Status == ConversationStatus.Open,
                cancellationToken);

    public async Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task<Conversation?> FindForUpdateAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task<(IReadOnlyList<Conversation> Items, int? TotalCount)> ListByCustomerAsync(
        Guid customerId, ConversationKind? kind, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .Where(c => c.CustomerId == customerId)
            .AsQueryable();

        if (kind is { } tipo)
        {
            query = query.Where(c => c.Kind == tipo);
        }

        query = query.OrderByDescending(c => c.OpenedAt).ThenBy(c => c.Id);

        return await PaginarAsync(query, pagina, cancellationToken);
    }

    public async Task<(IReadOnlyList<Conversation> Items, int? TotalCount)> ListAsync(
        ConversationStatus? status, ConversationKind? kind, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Conversations.AsNoTracking().Include(c => c.Messages).AsQueryable();

        if (status is { } estado)
        {
            query = query.Where(c => c.Status == estado);
        }

        if (kind is { } tipo)
        {
            query = query.Where(c => c.Kind == tipo);
        }

        query = query.OrderByDescending(c => c.OpenedAt).ThenBy(c => c.Id);

        return await PaginarAsync(query, pagina, cancellationToken);
    }

    /// <summary>
    /// A query já vem ordenada de forma determinística — só falta decidir se
    /// se corta (ADR-068).
    /// </summary>
    private static async Task<(IReadOnlyList<Conversation> Items, int? TotalCount)> PaginarAsync(
        IQueryable<Conversation> query, PageRequest? pagina, CancellationToken cancellationToken)
    {
        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        var itens = await query.ToListAsync(cancellationToken);
        return (itens, total);
    }

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken) =>
        await context.Conversations.AddAsync(conversation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
