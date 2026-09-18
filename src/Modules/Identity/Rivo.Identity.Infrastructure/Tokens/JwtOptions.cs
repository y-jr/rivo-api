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

    // A duração da sessão esteve aqui e saiu (ADR-067).
    //
    // Não era assunto do token: o token é um portador com prazo, a sessão é a
    // autorização — e a sessão passou a ter **dois** prazos, o absoluto e o de
    // inactividade. Vivem em `SessionPolicyOptions`, secção `Session`.
    //
    // A chave antiga `Jwt:SessionLifetimeMinutes` faz o arranque **falhar**, de
    // propósito, em vez de ser ignorada em silêncio: quem a tiver no `.env` ficaria
    // convencido de que continua a mandar na duração. Ver
    // `IdentityModuleExtensions`.
}
