using Microsoft.EntityFrameworkCore;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Domain.Sessions;
using Rivo.Identity.Infrastructure.Persistence;

namespace Rivo.Identity.Infrastructure.Sessions;

public sealed class SessionStore(RivoIdentityDbContext context) : ISessionStore
{
    public async Task AddAsync(Session session, CancellationToken cancellationToken) =>
        await context.Sessions.AddAsync(session, cancellationToken);

    public async Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await context.Sessions.FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListForUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.Sessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Rastreadas de propósito: a revogação passa pelo domínio
        // (`Session.Revoke`), que é idempotente e guarda o instante da
        // primeira. Um `ExecuteUpdate` seria mais rápido e saltava a regra.
        var activas = await context.Sessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var sessao in activas)
        {
            sessao.Revoke(now);
        }

        if (activas.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return activas.Count;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
