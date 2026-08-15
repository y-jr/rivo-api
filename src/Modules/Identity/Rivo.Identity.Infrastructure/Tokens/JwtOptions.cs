using System.ComponentModel.DataAnnotations;

namespace Rivo.Identity.Infrastructure.Tokens;

/// <summary>
/// Configuração da emissão e validação de JWT, lida da secção <c>Jwt</c>.
///
/// Validada no arranque (ValidateOnStart): uma chave em falta ou curta demais
/// é falha de configuração, e deve impedir a aplicação de subir em vez de
/// só rebentar no primeiro login.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Chave de assinatura HMAC-SHA256. Mínimo de 32 bytes por exigência do
    /// algoritmo. Nunca em código nem em ficheiro versionado — em produção vem
    /// de gestão de segredos.
    /// </summary>
    [Required]
    [MinLength(32, ErrorMessage = "A chave de assinatura tem de ter pelo menos 32 caracteres.")]
    public string SigningKey { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Duração da sessão e do token que a acompanha.
    ///
    /// Nota: isto é expiração <em>absoluta</em>. A expiração por inactividade
    /// que os requisitos preveem (15 min para perfis decisórios) ainda não está
    /// implementada — ver state/pending-decisions.md.
    /// </summary>
    [Range(1, 24 * 60)]
    public int SessionLifetimeMinutes { get; init; } = 60;
}
