using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Infrastructure.Identity;

/// <summary>
/// Verifica um ID token da Google contra as chaves públicas que a Google
/// publica (ADR-032).
///
/// <para>
/// Não há <c>ClientSecret</c> nenhum aqui, e isso não é omissão: validar uma
/// assinatura exige a chave <em>pública</em>. O segredo só faria falta no
/// fluxo de código de autorização, que o ADR-032 rejeitou.
/// </para>
/// </summary>
public sealed class GoogleIdentityVerifier : IExternalIdentityVerifier, IDisposable
{
    /// <summary>
    /// Documento de descoberta OIDC da Google. Dele sai o JWKS — e sai
    /// sozinho: fixar as chaves na configuração obrigaria a mexer no
    /// deployment sempre que a Google as rodasse, o que ela faz sem avisar.
    /// </summary>
    private const string MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

    /// <summary>
    /// Os dois emissores que a Google usa. Estão ambos documentados e ambos
    /// aparecem em tokens reais — aceitar só um provoca falhas intermitentes
    /// que ninguém consegue reproduzir.
    /// </summary>
    private static readonly string[] ValidIssuers =
    [
        "https://accounts.google.com",
        "accounts.google.com",
    ];

    private readonly ConfigurationManager<OpenIdConnectConfiguration> _metadata;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly GoogleAuthOptions _options;

    public GoogleIdentityVerifier(IOptions<GoogleAuthOptions> options)
    {
        _options = options.Value;

        // O ConfigurationManager cacheia o documento e as chaves, e volta a
        // buscá-los quando expiram ou quando uma validação falha por chave
        // desconhecida. Sem ele, cada login seria um pedido HTTP à Google.
        _metadata = new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<ExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(credential))
        {
            return null;
        }

        OpenIdConnectConfiguration configuration;

        try
        {
            configuration = await _metadata.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A Google inalcançável é indisponibilidade, não credencial
            // inválida. Devolve-se `null` na mesma — o caso de uso recusa a
            // entrada — porque a alternativa seria deixar passar sem validar.
            return null;
        }

        var result = await _handler.ValidateTokenAsync(credential, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = ValidIssuers,

            // A audiência é o nosso ClientId. É esta linha que impede aceitar
            // um ID token que a Google emitiu para outra aplicação qualquer.
            ValidateAudience = true,
            ValidAudience = _options.ClientId,

            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,

            ValidateLifetime = true,

            // Diverge dos `TimeSpan.Zero` do ADR-013 de propósito. No token do
            // Rivo quem assina e quem valida partilham o relógio, e zero é o
            // certo. Este vem de um relógio que não controlamos: sem folga,
            // uma deriva de segundos na VPS vira falhas de login intermitentes
            // e sem explicação visível.
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid)
        {
            return null;
        }

        var subject = ReadClaim(result, JwtRegisteredClaimNames.Sub);
        var email = ReadClaim(result, JwtRegisteredClaimNames.Email);

        // Um token válido sem `sub` ou sem `email` não dá para identificar
        // ninguém. Não devia acontecer com a Google; se acontecer, recusa-se.
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new ExternalIdentity(
            ExternalProviders.Google,
            subject,
            email,
            ReadEmailVerified(result));
    }

    private static string? ReadClaim(TokenValidationResult result, string type) =>
        result.Claims.TryGetValue(type, out var value) ? value?.ToString() : null;

    /// <summary>
    /// Lê `email_verified` tolerando as duas formas em que aparece.
    ///
    /// <para>
    /// A claim é booleana na especificação, e chega ora como <c>bool</c> ora
    /// como a string "true", conforme o caminho de desserialização. Um
    /// <c>is bool</c> simples devolveria falso para a string — e falso aqui
    /// significa recusar o login, ou seja, a falha seria "o Google não
    /// funciona" sem pista nenhuma da causa.
    /// </para>
    /// </summary>
    private static bool ReadEmailVerified(TokenValidationResult result) =>
        result.Claims.TryGetValue("email_verified", out var value) && value switch
        {
            bool verified => verified,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false,
        };

    public void Dispose() => (_metadata as IDisposable)?.Dispose();
}
