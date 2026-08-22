using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Abre uma sessão, emite o token que lhe pertence e regista a entrada.
///
/// <para>
/// <strong>É o troço comum a todos os caminhos de autenticação</strong> —
/// password e Google hoje, outro provider amanhã (ADR-032). Existe como peça
/// própria por uma razão concreta, e não por gosto de factorizar: há
/// requisitos por satisfazer que mexem exactamente aqui — expiração por
/// inactividade e sessão única reforçada
/// (<c>.claude/modules/identity.md</c>). Duplicado por caminho, cada um deles
/// teria de ser implementado tantas vezes quantos os métodos de login, e o
/// esquecido seria o menos usado.
/// </para>
///
/// <para>
/// A ordem importa: a sessão é criada <em>antes</em> do token, porque o token
/// transporta o identificador da sessão. Sem isso o token não seria revogável
/// (ADR-013).
/// </para>
/// </summary>
public sealed class SessionIssuer(
    ISessionStore sessions,
    IAccessTokenIssuer tokens,
    IAuditTrail audit,
    TimeProvider clock)
{
    /// <param name="method">
    /// Como é que o utilizador se autenticou, em
    /// <see cref="AuthenticationMethods"/>. Vai para a trilha, não para o token.
    /// </param>
    /// <param name="ipAddress">
    /// Origem do pedido. Registado na sessão por exigência de auditoria (BR-9)
    /// e obtido na camada API, que é quem conhece o transporte.
    /// </param>
    /// <param name="correlationId">Liga as acções deste pedido entre módulos.</param>
    public async Task<AccessToken> IssueAsync(
        AuthenticatedAccount account,
        string method,
        string ipAddress,
        string? userAgent,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var session = Session.Start(
            userId: account.UserId,
            ipAddress: ipAddress,
            userAgent: userAgent,
            now: clock.GetUtcNow(),
            lifetime: tokens.SessionLifetime);

        await sessions.AddAsync(session, cancellationToken);
        await sessions.SaveChangesAsync(cancellationToken);

        var token = tokens.Issue(account, session.Id, session.ExpiresAt);

        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserLoggedIn,
                AuditEntityTypes.User,
                account.UserId.ToString(),
                new AuditContext(account.UserId, ipAddress, correlationId),
                // O método vai em `new_value` e a acção mantém-se a mesma para
                // os dois caminhos: assim "todos os logins" continua a ser uma
                // consulta só, e quem investigar um acesso concreto continua a
                // saber por onde ele entrou (ADR-032).
                NewValue: $$"""{"method":"{{method}}"}"""),
            cancellationToken);

        return token;
    }
}
