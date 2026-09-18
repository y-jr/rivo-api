namespace Rivo.Identity.Api.Contracts;

// DTOs próprios da fronteira HTTP. As entidades de domínio nunca são expostas
// como modelos de transporte (architecture/dependency-rules.md).

// `RegisterRequest` saiu a 2026-09-13 com o registo publico (ADR-059).
public sealed record InviteUserRequest(string Email, string Profile);

/// <param name="Token">O testemunho do convite, como veio na ligacao.</param>
/// <param name="Email">
/// O endereço de quem perdeu a password. A resposta é a mesma tenha ou não conta
/// (ADR-065).
/// </param>
public sealed record RecoverPasswordRequest(string Email);

public sealed record CompletePasswordRecoveryRequest(Guid UserId, string Token, string Password);

public sealed record AcceptInvitationRequest(Guid UserId, string Token, string Password);

public sealed record LoginRequest(string Email, string Password);

/// <param name="IdToken">
/// ID token emitido pela Google ao frontend (ADR-032). É uma afirmação
/// assinada sobre quem o utilizador é — o servidor valida-a contra as chaves
/// públicas da Google antes de a aceitar.
/// </param>
public sealed record GoogleLoginRequest(string IdToken);

/// <param name="ExpiresAt">
/// O tecto absoluto da sessao. **Nao e quando o utilizador vai ser expulso** —
/// uma sessao parada morre antes, por inactividade.
/// </param>
/// <param name="IdleTimeoutSeconds">
/// Quanta inactividade a sessao tolera (ADR-067). O cliente deve usar isto para
/// avisar antes de expulsar: a inactividade passou a ser a causa mais comum de
/// fim de sessao, e e a unica que o utilizador pode evitar.
///
/// <para>
/// Depende de quem entra: quem tem autoridade para decidir aprovacoes recebe um
/// limite mais curto, por requisito.
/// </para>
/// </param>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    int IdleTimeoutSeconds);

/// <summary>Identidade do utilizador autenticado, para o cliente se orientar.</summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <param name="Profile">Nome do Perfil de Acesso, como consta de /identity/roles.</param>
public sealed record AssignRoleRequest(string Profile);

/// <param name="CurrentPassword">
/// Obrigatória. Sem ela, um token roubado mudava a password e trancava o dono
/// fora da sua própria conta.
/// </param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ResetPasswordRequest(string NewPassword);

/// <param name="Reason">
/// Obrigatória. Fecha ou reabre o acesso de alguém, e a trilha tem de dizer
/// porquê.
/// </param>
public sealed record SetAccountStatusRequest(bool Active, string Reason);
