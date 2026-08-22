using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.UseCases;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// Regras do login federado com Google (ADR-032).
///
/// <para>
/// O que aqui se testa não é a validação criptográfica do ID token — essa é de
/// infraestrutura e está do outro lado do <see cref="IExternalIdentityVerifier"/>.
/// É a política: quem entra, quem é recusado, e o que fica na trilha.
/// </para>
/// </summary>
public class LogInWithGoogleTests
{
    private const string Email = "decisor@rivo.ao";
    private const string Subject = "google-sub-12345";

    private static AuthenticatedAccount Account() =>
        new(Guid.NewGuid(), Email, ["Finance"], ["finance.payments.approve"]);

    private static ExternalIdentity Identity(bool emailVerified = true) =>
        new(ExternalProviders.Google, Subject, Email, emailVerified);

    private static (LogInWithGoogle UseCase, FakeUserAccounts Accounts, FakeAuditTrail Audit,
        FakeSessionStore Sessions, FakeAccessTokenIssuer Tokens) Build(
        ExternalIdentity? identity,
        AuthenticatedAccount? linkedAccount = null,
        AuthenticatedAccount? accountByEmail = null,
        bool configured = true)
    {
        var accounts = new FakeUserAccounts(linkedAccount, accountByEmail);
        var audit = new FakeAuditTrail();
        var sessions = new FakeSessionStore();
        var tokens = new FakeAccessTokenIssuer();

        var issuer = new SessionIssuer(sessions, tokens, audit, TimeProvider.System);
        var useCase = new LogInWithGoogle(
            new FakeExternalIdentityVerifier(identity, configured), accounts, issuer, audit);

        return (useCase, accounts, audit, sessions, tokens);
    }

    private static Task<GoogleLogInResult> ExecuteAsync(LogInWithGoogle useCase) =>
        useCase.ExecuteAsync("id-token", "203.0.113.7", "agente-de-teste", "correlacao", CancellationToken.None);

    /// <summary>
    /// O caminho normal: identidade verificada, ligação já existente.
    /// </summary>
    [Fact]
    public async Task Execute_WithLinkedAccount_IssuesToken()
    {
        var account = Account();
        var (useCase, accounts, audit, _, _) = Build(Identity(), linkedAccount: account);

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.AccessToken);

        // Já estava ligada: não se tenta ligar outra vez.
        Assert.Equal(0, accounts.LinkAttempts);
        Assert.True(audit.Has(AuditActions.UserLoggedIn));
    }

    /// <summary>
    /// O token emitido pelo caminho do Google pertence a uma sessão persistida,
    /// tal como o do login por password.
    ///
    /// <para>
    /// É a garantia central do ADR-032: sem sessão, o token seria irrevogável e
    /// perdia-se o ADR-013 por uma porta lateral.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Execute_OnSuccess_BindsTokenToPersistedSession()
    {
        var account = Account();
        var (useCase, _, audit, sessions, tokens) = Build(Identity(), linkedAccount: account);

        await ExecuteAsync(useCase);

        var session = Assert.Single(sessions.Added);
        Assert.Equal(account.UserId, session.UserId);

        // O token foi emitido para essa sessão, e não para nenhuma outra.
        Assert.Equal(session.Id, Assert.Single(tokens.IssuedForSessions));

        // BR-9: o IP de origem fica registado na sessão.
        Assert.Equal("203.0.113.7", session.IpAddress);
    }

    /// <summary>
    /// Primeira entrada: liga a identidade Google à conta existente com o mesmo
    /// e-mail, e regista essa ligação como acção própria.
    /// </summary>
    [Fact]
    public async Task Execute_FirstSignIn_LinksAccountAndAuditsTheLink()
    {
        var account = Account();
        var (useCase, accounts, audit, _, _) = Build(Identity(), accountByEmail: account);

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, accounts.LinkAttempts);

        // A ligação é alteração de estado com peso de segurança: tem acção
        // própria na trilha, distinta da entrada.
        Assert.True(audit.Has(AuditActions.ExternalLoginLinked));
        Assert.True(audit.Has(AuditActions.UserLoggedIn));
    }

    /// <summary>
    /// <strong>A regra que o ADR-016 protege:</strong> uma identidade Google
    /// perfeitamente válida sem conta Rivo correspondente não entra, e não
    /// cria conta nenhuma.
    /// </summary>
    [Fact]
    public async Task Execute_WithoutMatchingAccount_IsRejectedAndCreatesNothing()
    {
        var (useCase, _, audit, sessions, tokens) = Build(Identity());

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.Rejected, result.Outcome);
        Assert.Null(result.AccessToken);

        // Nem sessão, nem token, nem conta.
        Assert.Empty(sessions.Added);
        Assert.Empty(tokens.IssuedForSessions);
        Assert.False(audit.Has(AuditActions.ExternalLoginLinked));

        // BR-12: a tentativa falhada deixa rasto.
        Assert.True(audit.Has(AuditActions.UserLoginFailed));
    }

    /// <summary>
    /// E-mail que a Google não confirma não serve para encontrar conta nenhuma.
    ///
    /// <para>
    /// Sem esta recusa, bastaria registar num provider permissivo um e-mail
    /// igual ao de alguém do Rivo para lhe entrar na conta. A conta existe
    /// neste teste — o que a protege é só a verificação.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Execute_WithUnverifiedEmail_IsRejectedEvenWhenAccountExists()
    {
        var (useCase, accounts, audit, _, _) = Build(
            Identity(emailVerified: false), accountByEmail: Account());

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.Rejected, result.Outcome);

        // Não se chega sequer a procurar a conta por e-mail.
        Assert.Equal(0, accounts.LinkAttempts);
        Assert.True(audit.Has(AuditActions.UserLoginFailed));
    }

    /// <summary>
    /// Credencial que não passa a validação do provider é recusada, e a
    /// tentativa é auditada sem e-mail — nada do que um token inválido diz é
    /// de confiança.
    /// </summary>
    [Fact]
    public async Task Execute_WithInvalidCredential_IsRejectedAndAuditsWithoutEmail()
    {
        var (useCase, _, audit, _, _) = Build(identity: null, accountByEmail: Account());

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.Rejected, result.Outcome);

        var failure = Assert.Single(audit.Records, record => record.Action == AuditActions.UserLoginFailed);
        Assert.DoesNotContain(Email, failure.EntityId);
    }

    /// <summary>
    /// Ambiente sem Google configurado responde "não faço", e não "credencial
    /// errada" — a distinção é o que evita mandar procurar o defeito na conta
    /// de quem tentou entrar (ADR-032).
    /// </summary>
    [Fact]
    public async Task Execute_WhenProviderNotConfigured_ReportsNotConfigured()
    {
        var (useCase, _, audit, _, _) = Build(Identity(), linkedAccount: Account(), configured: false);

        var result = await ExecuteAsync(useCase);

        Assert.Equal(GoogleLogInOutcome.NotConfigured, result.Outcome);

        // Não houve tentativa de autenticação nenhuma: nada a auditar.
        Assert.Empty(audit.Records);
    }

    /// <summary>
    /// A trilha distingue por que caminho a pessoa entrou, sem fragmentar a
    /// acção — "todos os logins" continua a ser uma consulta só.
    /// </summary>
    [Fact]
    public async Task Execute_OnSuccess_RecordsAuthenticationMethod()
    {
        var (useCase, _, audit, _, _) = Build(Identity(), linkedAccount: Account());

        await ExecuteAsync(useCase);

        var login = Assert.Single(audit.Records, record => record.Action == AuditActions.UserLoggedIn);
        Assert.Contains(AuthenticationMethods.Google, login.NewValue);
    }
}
