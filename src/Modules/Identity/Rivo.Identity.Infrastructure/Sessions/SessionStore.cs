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

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
