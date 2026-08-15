using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Persistência de sessões. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface ISessionStore
{
    Task AddAsync(Session session, CancellationToken cancellationToken);

    Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Persiste alterações a uma sessão já materializada (ex.: revogação).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
