using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Autentica um utilizador, abre uma sessão e emite o token de acesso.
///
/// A ordem importa: a sessão é criada <em>antes</em> do token, porque o token
/// transporta o identificador da sessão. Sem isso o token não seria revogável.
/// </summary>
public sealed class LogIn(
    IUserAccounts accounts,
    ISessionStore sessions,
    IAccessTokenIssuer tokens,
    IAuditTrail audit,
    TimeProvider clock)
{
    /// <param name="ipAddress">
    /// Origem do pedido. Registado na sessão por exigência de auditoria (BR-9)
    /// e obtido na camada API, que é quem conhece o transporte.
    /// </param>
    /// <param name="correlationId">Liga as acções deste pedido entre módulos.</param>
    public async Task<LogInResult> ExecuteAsync(
        string email,
        string password,
        string ipAddress,
        string? userAgent,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.VerifyPasswordAsync(email, password, cancellationToken);

        if (account is null)
        {
            // Tentativa falhada é auditada (BR-12): sem isto, uma sequência de
            // tentativas contra uma conta não deixaria rasto nenhum.
            // A entidade é o e-mail tentado, porque não há utilizador conhecido.
            await audit.RecordAsync(
                new AuditRecord(
                    AuditActions.UserLoginFailed,
                    AuditEntityTypes.User,
                    email,
                    new AuditContext(null, ipAddress, correlationId)),
                cancellationToken);

            return LogInResult.Failed();
        }

        var now = clock.GetUtcNow();

        var session = Session.Start(
            userId: account.UserId,
            ipAddress: ipAddress,
            userAgent: userAgent,
            now: now,
            lifetime: tokens.SessionLifetime);

        await sessions.AddAsync(session, cancellationToken);
        await sessions.SaveChangesAsync(cancellationToken);

        var token = tokens.Issue(account, session.Id, session.ExpiresAt);

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserLoggedIn,
                AuditEntityTypes.User,
                account.UserId.ToString(),
                new AuditContext(account.UserId, ipAddress, correlationId)),
            cancellationToken);

        return LogInResult.Success(token.Value, token.ExpiresAt);
    }
}

public sealed record LogInResult(bool Succeeded, string? AccessToken, DateTimeOffset? ExpiresAt)
{
    public static LogInResult Success(string token, DateTimeOffset expiresAt) => new(true, token, expiresAt);

    /// <summary>
    /// Sem detalhe do motivo: distinguir "utilizador inexistente" de "password
    /// errada" revelaria que endereços estão registados.
    /// </summary>
    public static LogInResult Failed() => new(false, null, null);
}
