using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Autentica um utilizador por password, abre uma sessão e emite o token de
/// acesso.
///
/// <para>
/// Verificada a credencial, o resto — sessão, token, auditoria da entrada — é
/// o mesmo de qualquer outro caminho de autenticação e vive no
/// <see cref="SessionIssuer"/> (ADR-032). O que é próprio deste caso de uso é
/// só a verificação da password e o registo da tentativa falhada.
/// </para>
/// </summary>
public sealed class LogIn(
    IUserAccounts accounts,
    SessionIssuer sessions,
    IAuditTrail audit)
{
    /// <param name="ipAddress">
    /// Origem do pedido. Registado na sessão por exigência de auditoria (BR-9)
    /// e obtido na camada API, que é quem conhece o transporte.
    /// </param>
    /// <param name="correlationId">Liga as acções deste pedido entre módulos.</param>
    public async Task<LogInResult> ExecuteAsync(
        string email,
        string password,
        string ipAddress,
        string? userAgent,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.VerifyPasswordAsync(email, password, cancellationToken);

        if (account is null)
        {
            // Tentativa falhada é auditada (BR-12): sem isto, uma sequência de
            // tentativas contra uma conta não deixaria rasto nenhum.
            // A entidade é o e-mail tentado, porque não há utilizador conhecido.
            await audit.RecordAsync(
                new AuditRecord(
                    AuditActions.UserLoginFailed,
                    AuditEntityTypes.User,
                    email,
                    new AuditContext(null, ipAddress, correlationId),
                    NewValue: $$"""{"method":"{{AuthenticationMethods.Password}}"}"""),
                cancellationToken);

            return LogInResult.Failed();
        }

        var token = await sessions.IssueAsync(
            account,
            AuthenticationMethods.Password,
            ipAddress,
            userAgent,
            correlationId,
            cancellationToken);

        return LogInResult.Success(token.Value, token.ExpiresAt);
    }
}

public sealed record LogInResult(bool Succeeded, string? AccessToken, DateTimeOffset? ExpiresAt)
{
    public static LogInResult Success(string token, DateTimeOffset expiresAt) => new(true, token, expiresAt);

    /// <summary>
    /// Sem detalhe do motivo: distinguir "utilizador inexistente" de "password
    /// errada" revelaria que endereços estão registados.
    /// </summary>
    public static LogInResult Failed() => new(false, null, null);
}
