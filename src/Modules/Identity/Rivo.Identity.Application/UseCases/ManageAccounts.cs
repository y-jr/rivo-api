using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Notifications.Contracts;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Muda a password do próprio.
///
/// <para>
/// <strong>Revoga as outras sessões.</strong> Quem muda a password fá-lo quase
/// sempre por suspeitar que alguém a sabe — e deixar as sessões desse alguém
/// abertas esvaziava o acto. A sessão de quem está a mudar fica: seria estranho
/// ser expulso por se ter protegido.
/// </para>
/// </summary>
public sealed class ChangeOwnPassword(
    IUserAccounts accounts,
    ISessionStore sessions,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<PasswordChangeOutcome> ExecuteAsync(
        Guid userId,
        Guid currentSessionId,
        string currentPassword,
        string newPassword,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var resultado = await accounts.ChangePasswordAsync(
            userId, currentPassword, newPassword, cancellationToken);

        if (resultado.Result is not PasswordChangeResult.Changed)
        {
            // Uma tentativa falhada com a password actual errada fica na trilha:
            // uma sequência delas é a assinatura de quem tem o token e não tem
            // a credencial.
            if (resultado.Result is PasswordChangeResult.WrongCurrentPassword)
            {
                await audit.RecordAsync(
                    new AuditRecord(
                        AuditActions.PasswordChangeRefused,
                        AuditEntityTypes.User,
                        userId.ToString(),
                        context),
                    cancellationToken);
            }

            return resultado;
        }

        var revogadas = await RevokeOtherSessionsAsync(userId, currentSessionId, cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.PasswordChanged,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"sessionsRevoked":{{revogadas}}}"""),
            cancellationToken);

        return resultado;
    }

    private async Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        var abertas = await sessions.ListForUserAsync(userId, cancellationToken);
        var agora = clock.GetUtcNow();
        var contadas = 0;

        foreach (var sessao in abertas.Where(s => s.Id != currentSessionId && s.IsActiveAt(agora)))
        {
            sessao.Revoke(agora);
            contadas++;
        }

        if (contadas > 0)
        {
            await sessions.SaveChangesAsync(cancellationToken);
        }

        return contadas;
    }
}

/// <summary>
/// Repõe a password de outra conta.
///
/// <para>
/// <strong>Revoga todas as sessões, sem excepção.</strong> Ao contrário da
/// mudança feita pelo próprio, aqui não há sessão a poupar — quem administra
/// não está dentro da conta, e se estivesse era esse o problema.
/// </para>
/// </summary>
public sealed class ResetUserPassword(
    IUserAccounts accounts,
    ISessionStore sessions,
    IAuditTrail audit,
    INotifier notifier,
    TimeProvider clock)
{
    public async Task<PasswordChangeOutcome> ExecuteAsync(
        Guid userId,
        string newPassword,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var resultado = await accounts.ResetPasswordAsync(userId, newPassword, cancellationToken);

        if (resultado.Result is not PasswordChangeResult.Changed)
        {
            return resultado;
        }

        var revogadas = await sessions.RevokeAllForUserAsync(userId, clock.GetUtcNow(), cancellationToken);

        // Acção própria na trilha, e não uma mudança de password qualquer: é o
        // caminho por onde uma conta é tomada, e quem audita tem de o encontrar
        // sem o procurar no meio das mudanças legítimas.
        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.PasswordReset,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"sessionsRevoked":{{revogadas}}}"""),
            cancellationToken);

        // O dono da conta é avisado. Se não foi ele a pedir, é assim que fica a
        // saber — e a notificação chega antes de quem repôs a poder usá-la.
        await notifier.QueueAsync(
            new NotificationRequest(
                RecipientUserId: userId,
                Type: NotificationTypes.AccessProfileAssigned,
                Title: "Password reposta",
                Message: "A sua password foi reposta por um administrador e as suas sessões foram terminadas."),
            cancellationToken);

        return resultado;
    }
}

/// <summary>
/// Activa ou desactiva uma conta.
///
/// <para>
/// <strong>É o que faltava para cortar o acesso a quem sai da empresa.</strong>
/// Até aqui, uma conta criada ficava a poder entrar para sempre — não havia
/// rota nenhuma que o impedisse.
/// </para>
/// </summary>
public sealed class SetAccountStatus(
    IUserAccounts accounts,
    ISessionStore sessions,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<AccountStatusOutcome> ExecuteAsync(
        Guid userId,
        bool active,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var resultado = await accounts.SetActiveAsync(userId, active, cancellationToken);

        if (resultado is not AccountStatusOutcome.Changed)
        {
            return resultado;
        }

        var revogadas = 0;

        // Desactivar sem terminar as sessões abertas deixaria a conta fechada à
        // entrada e aberta por dentro, até o último token expirar.
        if (!active)
        {
            revogadas = await sessions.RevokeAllForUserAsync(userId, clock.GetUtcNow(), cancellationToken);
        }

        await audit.RecordAsync(
            new AuditRecord(
                active ? AuditActions.AccountReactivated : AuditActions.AccountDeactivated,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"reason":"{{reason}}","sessionsRevoked":{{revogadas}}}"""),
            cancellationToken);

        return resultado;
    }
}

/// <summary>Retira um Perfil de Acesso a um utilizador.</summary>
public sealed class RemoveAccessProfile(IUserAccounts accounts, IAuditTrail audit)
{
    public async Task<AssignProfileOutcome> ExecuteAsync(
        Guid userId,
        string profile,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var resultado = await accounts.RemoveProfileAsync(userId, profile, cancellationToken);

        if (resultado is not AssignProfileOutcome.Assigned)
        {
            return resultado;
        }

        // Auditada pela mesma razão que a atribuição (BR-13): altera o que
        // alguém pode fazer.
        //
        // ⚠ **O efeito não é imediato no token que a pessoa já tem.** As
        // permissões são resolvidas na autenticação (ADR-014), e o token
        // corrente continua a levar as antigas até expirar. Para cortar já,
        // desactiva-se a conta, que revoga as sessões.
        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.ProfileRemoved,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                PreviousValue: $$"""{"profile":"{{profile}}"}"""),
            cancellationToken);

        return resultado;
    }
}

/// <summary>Sessões do próprio utilizador.</summary>
public sealed class ListOwnSessions(ISessionStore sessions, TimeProvider clock)
{
    public async Task<IReadOnlyList<SessionView>> ExecuteAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        var agora = clock.GetUtcNow();

        return
        [
            .. (await sessions.ListForUserAsync(userId, cancellationToken))
                .Select(s => new SessionView(
                    s.Id,
                    s.IpAddress,
                    s.UserAgent,
                    s.CreatedAt,
                    s.ExpiresAt,
                    s.RevokedAt,
                    s.IsActiveAt(agora),

                    // Marcar a corrente evita o engano mais fácil desta lista:
                    // terminar a sessão de onde se está a olhar para ela.
                    s.Id == currentSessionId)),
        ];
    }
}

/// <param name="IpAddress">
/// ⚠ Atrás de proxy é o do proxy, não o do cliente — ver o K8.
/// </param>
/// <param name="IsCurrent">Verdadeiro para a sessão de onde este pedido veio.</param>
public sealed record SessionView(
    Guid SessionId,
    string IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsActive,
    bool IsCurrent);

/// <summary>
/// Termina uma sessão do próprio utilizador.
///
/// <para>
/// <strong>Só as próprias.</strong> Revogar a sessão de outra pessoa a partir
/// daqui seria expulsá-la do sistema sem passar por permissão nenhuma — o
/// caminho para isso é desactivar a conta, que exige `identity.users.write`.
/// </para>
/// </summary>
public sealed class RevokeOwnSession(ISessionStore sessions, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RevokeSessionOutcome> ExecuteAsync(
        Guid userId,
        Guid sessionId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var sessao = await sessions.FindAsync(sessionId, cancellationToken);

        // Sessão de outra pessoa devolve o mesmo que sessão inexistente: dizer
        // "existe, mas não é sua" confirmaria a existência de um identificador
        // a quem não tem nada a ver com ele.
        if (sessao is null || sessao.UserId != userId)
        {
            return RevokeSessionOutcome.NotFound;
        }

        sessao.Revoke(clock.GetUtcNow());
        await sessions.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserLoggedOut,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"session":"{{sessionId}}"}"""),
            cancellationToken);

        return RevokeSessionOutcome.Revoked;
    }
}

public enum RevokeSessionOutcome
{
    Revoked,
    NotFound,
}
