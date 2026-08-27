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

    /// <summary>
    /// Sessões de um utilizador, mais recentes primeiro. Inclui as revogadas e
    /// as expiradas: quem vê a lista precisa de saber de onde entrou, e não só
    /// de onde está.
    /// </summary>
    Task<IReadOnlyList<Session>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revoga todas as sessões activas de um utilizador.
    ///
    /// <para>
    /// É o que dá efeito imediato a desactivar uma conta ou a repor uma
    /// password. Sem isto, um token já emitido continuava a servir até expirar
    /// — e a conta ficava desactivada no papel e aberta na prática.
    /// </para>
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Persiste alterações a uma sessão já materializada (ex.: revogação).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
