namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Verificação de uma identidade afirmada por um provider externo (ADR-032).
///
/// <para>
/// Existe pela mesma razão que <see cref="IAccessTokenIssuer"/>: a camada
/// Application decide <em>o que fazer</em> com uma identidade externa; validar
/// criptograficamente a afirmação do provider é infraestrutura, e fica do
/// outro lado desta interface. Sem ela, `Application` teria de conhecer JWKS,
/// documentos de descoberta OIDC e o formato de token da Google — precisamente
/// o acoplamento que `docs` §D1 manda evitar ao classificar autenticação como
/// infraestrutura delegável.
/// </para>
/// </summary>
public interface IExternalIdentityVerifier
{
    /// <summary>
    /// Falso quando o provider não está configurado neste ambiente.
    ///
    /// <para>
    /// É deliberadamente distinto de "a credencial não serve": o caso de uso
    /// precisa de os separar para que a API possa responder 501 em vez de 401
    /// (ADR-032). Um ambiente sem `Google:ClientId` a devolver 401 mandaria
    /// procurar o defeito na conta do utilizador, que é o sítio errado.
    /// </para>
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Valida a credencial emitida pelo provider e devolve a identidade que
    /// ela afirma.
    /// </summary>
    /// <returns>
    /// <c>null</c> quando a credencial não é válida — assinatura, emissor,
    /// audiência ou validade. Sem detalhe do motivo, pela mesma razão que
    /// <see cref="IUserAccounts.VerifyPasswordAsync"/>: distinguir os casos
    /// diria a um atacante o que corrigir a seguir.
    /// </returns>
    Task<ExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken);
}

/// <summary>Identidade afirmada por um provider externo, já verificada.</summary>
/// <param name="Provider">Provider que a emitiu, em <see cref="ExternalProviders"/>.</param>
/// <param name="Subject">
/// Identificador estável da pessoa junto do provider (`sub`). É por aqui que
/// se reconhece quem volta — e não pelo e-mail, que pode mudar.
/// </param>
/// <param name="Email">Endereço afirmado pelo provider.</param>
/// <param name="EmailVerified">
/// Se o provider confirma ser dono do endereço. <strong>Falso significa
/// recusar</strong>: ligar uma conta por um e-mail não verificado seria via de
/// tomada de conta (ADR-032).
/// </param>
public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string Email,
    bool EmailVerified);

/// <summary>
/// Providers externos suportados. Constantes em vez de texto livre: o valor é
/// escrito em `identity.app_user_login` e uma gralha criaria uma ligação que
/// nunca mais é encontrada.
/// </summary>
public static class ExternalProviders
{
    public const string Google = "Google";
}

/// <summary>
/// Como é que o utilizador provou ser quem diz. Registado na auditoria para
/// que a trilha distinga os dois caminhos sem os separar em acções diferentes
/// (ADR-032).
/// </summary>
public static class AuthenticationMethods
{
    public const string Password = "password";
    public const string Google = "google";
}
