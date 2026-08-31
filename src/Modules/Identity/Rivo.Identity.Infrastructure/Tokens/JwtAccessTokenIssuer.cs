using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rivo.Identity.Application.Abstractions;
using ApplicationPermissions = Rivo.Identity.Contracts.IdentityPermissions;

namespace Rivo.Identity.Infrastructure.Tokens;

public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(_options.SessionLifetimeMinutes);

    public AccessToken Issue(AuthenticatedAccount account, Guid sessionId, DateTimeOffset expiresAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, account.Email),

            // "sid" liga o token à sessão persistida. É o que permite revogar:
            // a cada pedido confirma-se que a sessão continua activa.
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString()),

            // "jti" dá identidade única ao token, útil para rastreio em auditoria.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        // Perfil de Acesso → claim de role, para que [Authorize(Roles = ...)]
        // funcione sem consultar a base de dados.
        claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Permissões consolidadas dos perfis. Vão no token para que a
        // verificação de autorização seja uma comparação em memória.
        //
        // Contrapartida: alterar as permissões de um perfil só se reflecte no
        // login seguinte. Para forçar, revoga-se a sessão (ADR-014).
        claims.AddRange(account.Permissions.Select(
            permission => new Claim(ApplicationPermissions.ClaimType, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
