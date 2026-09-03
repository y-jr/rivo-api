using Microsoft.EntityFrameworkCore;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Infrastructure.Persistence;

public sealed class ConversationStore(MessagingDbContext context) : IConversationStore
{
    public async Task<Conversation?> FindOpenByCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(
                c => c.CustomerId == customerId && c.Status == ConversationStatus.Open, cancellationToken);

    public async Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task<Conversation?> FindForUpdateAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByCustomerAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        await context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListAsync(
        ConversationStatus? status, CancellationToken cancellationToken)
    {
        var query = context.Conversations.AsNoTracking().Include(c => c.Messages).AsQueryable();

        if (status is { } estado)
        {
            query = query.Where(c => c.Status == estado);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken) =>
        await context.Conversations.AddAsync(conversation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
