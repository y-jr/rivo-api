using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Autentica um utilizador por uma identidade da Google (ADR-032).
///
/// <para>
/// <strong>A Google autentica; a sessão continua a ser do Rivo.</strong> Este
/// caso de uso desagua no mesmo <see cref="SessionIssuer"/> que o login por
/// password — emitir um token à margem dele seria abdicar da revogação
/// (ADR-013), do IP na trilha (BR-9) e da auditoria da entrada.
/// </para>
///
/// <para>
/// <strong>Nunca cria contas.</strong> Uma identidade Google válida sem conta
/// Rivo correspondente é recusada, porque a existência de uma conta é acto
/// deliberado de quem administra (ADR-016).
/// </para>
/// </summary>
public sealed class LogInWithGoogle(
    IExternalIdentityVerifier verifier,
    IUserAccounts accounts,
    SessionIssuer sessions,
    IAuditTrail audit)
{
    public async Task<GoogleLogInResult> ExecuteAsync(
        string idToken,
        string ipAddress,
        string? userAgent,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        // Ambiente sem Google configurado. Separado da recusa de propósito: a
        // API responde 501, e não 401, para não mandar procurar o defeito na
        // conta de quem tentou entrar.
        if (!verifier.IsConfigured)
        {
            return GoogleLogInResult.NotConfigured();
        }

        var identity = await verifier.VerifyAsync(idToken, cancellationToken);

        if (identity is null)
        {
            // BR-12: a tentativa falhada deixa rasto. Não há e-mail para
            // registar — o token não passou a validação, portanto nada do que
            // ele diz é de confiança, incluindo o endereço.
            await RecordFailureAsync("google:credencial_invalida", ipAddress, correlationId, cancellationToken);
            return GoogleLogInResult.Rejected();
        }

        // Um endereço que o provider não confirma não serve para encontrar
        // conta nenhuma: seria bastar registar num provider permissivo um
        // e-mail igual ao de alguém do Rivo (ADR-032).
        if (!identity.EmailVerified)
        {
            await RecordFailureAsync(identity.Email, ipAddress, correlationId, cancellationToken);
            return GoogleLogInResult.Rejected();
        }

        // Caminho normal: já houve uma primeira entrada, e a ligação existe.
        // Procura-se por `sub`, que não muda se a pessoa trocar de e-mail.
        var account = await accounts.FindByExternalLoginAsync(
            identity.Provider, identity.Subject, cancellationToken);

        if (account is null)
        {
            account = await LinkOnFirstSignInAsync(identity, ipAddress, correlationId, cancellationToken);

            if (account is null)
            {
                return GoogleLogInResult.Rejected();
            }
        }

        var token = await sessions.IssueAsync(
            account,
            AuthenticationMethods.Google,
            ipAddress,
            userAgent,
            correlationId,
            cancellationToken);

        return GoogleLogInResult.Success(token.Value, token.ExpiresAt);
    }

    /// <summary>
    /// Primeira entrada: liga a identidade Google à conta Rivo com o mesmo
    /// e-mail. Devolve <c>null</c> quando não há conta — que é recusa por
    /// política, não falha técnica.
    /// </summary>
    private async Task<AuthenticatedAccount?> LinkOnFirstSignInAsync(
        ExternalIdentity identity,
        string ipAddress,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var link = await accounts.LinkExternalLoginAsync(
            identity.Email, identity.Provider, identity.Subject, cancellationToken);

        if (link.Outcome != LinkExternalLoginOutcome.Linked)
        {
            await RecordFailureAsync(identity.Email, ipAddress, correlationId, cancellationToken);
            return null;
        }

        var account = link.Account!;

        // A ligação é auditada por si, e não confundida com a entrada: a conta
        // ganhou um caminho de credencial novo, que é alteração de estado com
        // peso de segurança. Acontece uma vez por pessoa.
        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.ExternalLoginLinked,
                AuditEntityTypes.User,
                account.UserId.ToString(),
                new AuditContext(account.UserId, ipAddress, correlationId),
                NewValue: $$"""{"provider":"{{identity.Provider}}"}"""),
            cancellationToken);

        return account;
    }

    private Task RecordFailureAsync(
        string attempted,
        string ipAddress,
        string? correlationId,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(
            new AuditRecord(
                AuditActions.UserLoginFailed,
                AuditEntityTypes.User,
                attempted,
                new AuditContext(null, ipAddress, correlationId),
                NewValue: $$"""{"method":"{{AuthenticationMethods.Google}}"}"""),
            cancellationToken);
}

/// <param name="Outcome">
/// Três resultados e não dois: "o Google não está ligado neste ambiente" não é
/// o mesmo que "esta credencial não serve", e a API tem de os distinguir.
/// </param>
public sealed record GoogleLogInResult(
    GoogleLogInOutcome Outcome,
    string? AccessToken,
    DateTimeOffset? ExpiresAt)
{
    public static GoogleLogInResult Success(string token, DateTimeOffset expiresAt) =>
        new(GoogleLogInOutcome.Succeeded, token, expiresAt);

    /// <summary>
    /// Sem detalhe do motivo: distinguir "token inválido" de "não há conta com
    /// este e-mail" diria a quem tenta o que corrigir a seguir.
    /// </summary>
    public static GoogleLogInResult Rejected() => new(GoogleLogInOutcome.Rejected, null, null);

    public static GoogleLogInResult NotConfigured() => new(GoogleLogInOutcome.NotConfigured, null, null);
}

public enum GoogleLogInOutcome
{
    Succeeded,
    Rejected,
    NotConfigured,
}
