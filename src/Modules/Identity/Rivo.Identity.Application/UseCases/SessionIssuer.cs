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
    ISessionPolicy policy,
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
    public async Task<IssuedSession> IssueAsync(
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
            lifetime: policy.AbsoluteLifetime,

            // A tolerância a inactividade depende de quem se autentica: quem
            // decide aprovações tem um limite mais curto. Resolvida aqui, no
            // login, e congelada na sessão — ver `Session.IdleTimeoutSeconds`
            // para a razão de não ser lida da configuração a cada pedido.
            idleTimeout: policy.IdleTimeoutFor(account.Permissions));

        await sessions.AddAsync(session, cancellationToken);
        await sessions.SaveChangesAsync(cancellationToken);

        // O prazo do token e o tecto absoluto da sessao, nao o de inactividade.
        // A inactividade e verificada no servidor a cada pedido, e um token com
        // prazo curto obrigaria a um mecanismo de renovacao para dar o mesmo
        // resultado -- mais pecas para a mesma garantia.
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

        return new IssuedSession(token, session.IdleTimeoutSeconds, session.EffectiveExpiry);
    }
}

/// <summary>
/// O que sai de um login: o token, e o que o cliente precisa de saber para nao
/// expulsar ninguem de surpresa.
///
/// <para>
/// <strong>A janela de inactividade vai para o cliente de proposito.</strong> Sem
/// ela, o cliente so conhece o prazo absoluto do token e nao tem como avisar
/// antes de a sessao morrer por inactividade -- que passa a ser a causa mais
/// comum de expulsao. Avisar e o que separa "a sessao expirou" de "perdi o que
/// estava a escrever".
/// </para>
/// </summary>
/// <param name="EffectiveExpiry">
/// O mais proximo dos dois prazos no momento da emissao. No login e sempre o de
/// inactividade, porque a sessao acaba de nascer.
/// </param>
public sealed record IssuedSession(
    AccessToken Token,
    int IdleTimeoutSeconds,
    DateTimeOffset EffectiveExpiry);
