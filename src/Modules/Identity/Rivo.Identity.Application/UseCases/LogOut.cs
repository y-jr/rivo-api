using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Termina uma sessão.
///
/// É o que dá sentido a persistir sessões: sem revogação, um JWT emitido
/// continuaria válido até expirar, mesmo depois de o utilizador sair.
/// </summary>
public sealed class LogOut(ISessionStore sessions, IAuditTrail audit, TimeProvider clock)
{
    public async Task ExecuteAsync(Guid sessionId, AuditContext context, CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(sessionId, cancellationToken);

        // Sessão inexistente ou já revogada não é erro: o efeito pretendido —
        // a sessão não serve para mais nada — já se verifica.
        if (session is null)
        {
            return;
        }

        session.Revoke(clock.GetUtcNow());
        await sessions.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserLoggedOut,
                AuditEntityTypes.User,
                session.UserId.ToString(),
                context),
            cancellationToken);
    }
}
