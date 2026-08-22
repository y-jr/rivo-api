namespace Rivo.Identity.Api.Contracts;

// DTOs próprios da fronteira HTTP. As entidades de domínio nunca são expostas
// como modelos de transporte (architecture/dependency-rules.md).

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

/// <param name="IdToken">
/// ID token emitido pela Google ao frontend (ADR-032). É uma afirmação
/// assinada sobre quem o utilizador é — o servidor valida-a contra as chaves
/// públicas da Google antes de a aceitar.
/// </param>
public sealed record GoogleLoginRequest(string IdToken);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Identidade do utilizador autenticado, para o cliente se orientar.</summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <param name="Profile">Nome do Perfil de Acesso, como consta de /identity/roles.</param>
public sealed record AssignRoleRequest(string Profile);
