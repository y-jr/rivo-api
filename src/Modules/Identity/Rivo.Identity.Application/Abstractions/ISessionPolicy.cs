namespace Rivo.Identity.Application.Abstractions;

/// <summary>
/// Quanto tempo dura uma sessão, e quanta inactividade tolera.
///
/// <para>
/// Interface própria, e não mais duas propriedades em
/// <see cref="IAccessTokenIssuer"/>: a duração da sessão deixou de ser um
/// detalhe do token. O token é um portador com prazo; a sessão é a autorização
/// em si, e tem regras — incluindo uma que depende de <em>quem</em> se autentica.
/// </para>
/// </summary>
public interface ISessionPolicy
{
    /// <summary>O tecto absoluto. Não desliza com a actividade.</summary>
    TimeSpan AbsoluteLifetime { get; }

    /// <summary>
    /// Quanta inactividade a sessão deste utilizador tolera.
    ///
    /// <para>
    /// <strong>Depende das permissões, não do nome do perfil.</strong> O requisito
    /// fala de «perfis decisórios» e os documentos deixam em aberto se o limite
    /// deve ser uniforme ou por perfil. Resolver por permissão — quem pode decidir
    /// aprovações — é mais estável do que uma lista de nomes: sobrevive a
    /// renomear um perfil, e a um perfil novo que ganhe autoridade de decisão sem
    /// ninguém se lembrar de o acrescentar aqui.
    /// </para>
    /// </summary>
    TimeSpan IdleTimeoutFor(IReadOnlyList<string> permissions);
}
