using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Application.Tests;

/// <summary>
/// Duplos escritos à mão, sem biblioteca de mocks — ADR-022 rejeitou
/// dependências que não resolvem problema nenhum, e estes contratos são
/// estreitos de propósito.
/// </summary>
internal sealed class FakeExternalIdentityVerifier : IExternalIdentityVerifier
{
    private readonly ExternalIdentity? _identity;

    public FakeExternalIdentityVerifier(ExternalIdentity? identity, bool configured = true)
    {
        _identity = identity;
        IsConfigured = configured;
    }

    public bool IsConfigured { get; }

    public Task<ExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken) =>
        Task.FromResult(_identity);
}

internal sealed class FakeUserAccounts : IUserAccounts
{
    private readonly AuthenticatedAccount? _linkedAccount;
    private readonly AuthenticatedAccount? _accountByEmail;

    public FakeUserAccounts(
        AuthenticatedAccount? linkedAccount = null,
        AuthenticatedAccount? accountByEmail = null)
    {
        _linkedAccount = linkedAccount;
        _accountByEmail = accountByEmail;
    }

    /// <summary>Quantas vezes se tentou ligar uma identidade externa a uma conta.</summary>
    public int LinkAttempts { get; private set; }

    public Task<AuthenticatedAccount?> FindByExternalLoginAsync(
        string provider, string providerKey, CancellationToken cancellationToken) =>
        Task.FromResult(_linkedAccount);

    public Task<LinkExternalLoginResult> LinkExternalLoginAsync(
        string email, string provider, string providerKey, CancellationToken cancellationToken)
    {
        LinkAttempts++;

        return Task.FromResult(_accountByEmail is null
            ? LinkExternalLoginResult.AccountNotFound()
            : LinkExternalLoginResult.Linked(_accountByEmail));
    }

    public Task<CreateAccountOutcome> CreateAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado nestes testes.");

    public Task<AuthenticatedAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado nestes testes.");

    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado nestes testes.");

    public Task<AssignProfileOutcome> AssignProfileAsync(Guid userId, string profile, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado nestes testes.");
}

internal sealed class FakeSessionStore : ISessionStore
{
    public List<Session> Added { get; } = [];

    public Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        Added.Add(session);
        return Task.CompletedTask;
    }

    public Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Added.FirstOrDefault(session => session.Id == sessionId));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public TimeSpan SessionLifetime => TimeSpan.FromHours(1);

    /// <summary>Sessões para que se emitiu token. É o que prova que o caminho desaguou no ADR-013.</summary>
    public List<Guid> IssuedForSessions { get; } = [];

    public AccessToken Issue(AuthenticatedAccount account, Guid sessionId, DateTimeOffset expiresAt)
    {
        IssuedForSessions.Add(sessionId);
        return new AccessToken($"token-{sessionId}", expiresAt);
    }
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Records { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }

    public bool Has(string action) => Records.Any(record => record.Action == action);
}
