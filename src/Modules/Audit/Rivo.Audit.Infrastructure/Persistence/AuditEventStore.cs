using Microsoft.EntityFrameworkCore;
using Rivo.Audit.Application;
using Rivo.Audit.Domain;

namespace Rivo.Audit.Infrastructure.Persistence;

public sealed class AuditEventStore(AuditDbContext context) : IAuditEventStore
{
    public async Task AddAsync(AuditEvent entry, CancellationToken cancellationToken) =>
        await context.Events.AddAsync(entry, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        string? entityType,
        string? entityId,
        int limit,
        CancellationToken cancellationToken)
    {
        // AsNoTracking: a trilha é imutável, seguir alterações não serve de nada.
        var query = context.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(entry => entry.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(entry => entry.EntityId == entityId);
        }

        return await query
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
