namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Emissão do token de acesso. A camada Application decide <em>quando</em> se
/// emite um token; o formato concreto (JWT, assinatura, emissor) é detalhe de
/// infraestrutura e fica do outro lado desta interface.
/// </summary>
public interface IAccessTokenIssuer
{
    /// <param name="sessionId">
    /// Vai dentro do token para que cada pedido possa confirmar que a sessão
    /// ainda está activa. É isto que torna o token revogável.
    /// </param>
    AccessToken Issue(AuthenticatedAccount account, Guid sessionId, DateTimeOffset expiresAt);

    // `SessionLifetime` esteve aqui e saiu (ADR-067). Quanto tempo uma sessão
    // dura é regra de autorização, não formato de token — vive em
    // `ISessionPolicy`, que também sabe que o limite depende de quem entra.
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
