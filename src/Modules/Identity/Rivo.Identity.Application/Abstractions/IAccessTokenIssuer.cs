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

    /// <summary>Duração configurada para a sessão e para o token que a acompanha.</summary>
    TimeSpan SessionLifetime { get; }
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
