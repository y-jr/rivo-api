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

    /// <summary>
    /// Um `UPDATE` condicional, e nada mais.
    ///
    /// <para>
    /// Sem <c>ExecuteUpdate</c> isto seria ler a sessão rastreada, chamar
    /// <c>Touch</c> e gravar — três coisas onde só é preciso uma, e com o
    /// contador de concorrência pelo meio a transformar dois pedidos paralelos
    /// numa falha. Ver a nota no contrato.
    /// </para>
    ///
    /// <para>
    /// A condição da janela está no <c>Where</c> e não em C# de propósito: assim
    /// duas chamadas simultâneas resolvem-se na base de dados — a segunda não
    /// encontra linha para actualizar — em vez de ambas decidirem que sim.
    /// </para>
    /// </summary>
    public async Task TouchAsync(
        Guid sessionId,
        DateTimeOffset now,
        TimeSpan resolution,
        CancellationToken cancellationToken)
    {
        var limite = now - resolution;

        await context.Sessions
            .Where(session => session.Id == sessionId && session.LastSeenAt < limite)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(session => session.LastSeenAt, now),
                cancellationToken);
    }
}
