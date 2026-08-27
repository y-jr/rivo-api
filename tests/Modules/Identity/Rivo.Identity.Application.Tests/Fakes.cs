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

    /// <summary>
    /// Estado em memória para os casos de uso de conta. Guarda-se em vez de se
    /// devolver valor fixo: metade do que há para testar é o efeito de uma
    /// operação sobre a seguinte — mudar a password e depois verificá-la.
    /// </summary>
    public Dictionary<Guid, string> Passwords { get; } = [];

    public HashSet<Guid> Deactivated { get; } = [];

    public Dictionary<Guid, HashSet<string>> Profiles { get; } = [];

    public Task<PasswordChangeOutcome> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        if (!Passwords.TryGetValue(userId, out var actual))
        {
            return Task.FromResult(PasswordChangeOutcome.UserNotFound());
        }

        if (actual != currentPassword)
        {
            return Task.FromResult(PasswordChangeOutcome.WrongCurrentPassword());
        }

        // Regra mínima, só para haver um caminho de recusa que não seja a
        // password actual errada.
        if (newPassword.Length < 8)
        {
            return Task.FromResult(PasswordChangeOutcome.Rejected(["Password demasiado curta."]));
        }

        Passwords[userId] = newPassword;
        return Task.FromResult(PasswordChangeOutcome.Changed());
    }

    public Task<PasswordChangeOutcome> ResetPasswordAsync(
        Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        if (!Passwords.ContainsKey(userId))
        {
            return Task.FromResult(PasswordChangeOutcome.UserNotFound());
        }

        Passwords[userId] = newPassword;
        return Task.FromResult(PasswordChangeOutcome.Changed());
    }

    public Task<AccountStatusOutcome> SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken)
    {
        if (!Passwords.ContainsKey(userId))
        {
            return Task.FromResult(AccountStatusOutcome.UserNotFound);
        }

        if (active)
        {
            Deactivated.Remove(userId);
        }
        else
        {
            Deactivated.Add(userId);
        }

        return Task.FromResult(AccountStatusOutcome.Changed);
    }

    public Task<AssignProfileOutcome> RemoveProfileAsync(
        Guid userId, string profile, CancellationToken cancellationToken)
    {
        if (!Passwords.ContainsKey(userId))
        {
            return Task.FromResult(AssignProfileOutcome.UserNotFound);
        }

        if (Profiles.TryGetValue(userId, out var perfis))
        {
            perfis.Remove(profile);
        }

        return Task.FromResult(AssignProfileOutcome.Assigned);
    }

    public Task<AssignProfileOutcome> AssignProfileAsync(Guid userId, string profile, CancellationToken cancellationToken)
    {
        if (!Passwords.ContainsKey(userId))
        {
            return Task.FromResult(AssignProfileOutcome.UserNotFound);
        }

        if (!Profiles.TryGetValue(userId, out var perfis))
        {
            perfis = [];
            Profiles[userId] = perfis;
        }

        perfis.Add(profile);
        return Task.FromResult(AssignProfileOutcome.Assigned);
    }
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

    public Task<IReadOnlyList<Session>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(
            [.. Added.Where(session => session.UserId == userId).OrderByDescending(s => s.CreatedAt)]);

    public Task<int> RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var activas = Added.Where(s => s.UserId == userId && s.IsActiveAt(now)).ToList();

        foreach (var sessao in activas)
        {
            sessao.Revoke(now);
        }

        return Task.FromResult(activas.Count);
    }

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

internal sealed class FakeNotifier : Rivo.Notifications.Contracts.INotifier
{
    public List<Rivo.Notifications.Contracts.NotificationRequest> Queued { get; } = [];

    public Task QueueAsync(
        Rivo.Notifications.Contracts.NotificationRequest request,
        CancellationToken cancellationToken)
    {
        Queued.Add(request);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Relógio parado. Escrito à mão em vez de trazer
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>, pela mesma razão que em
/// `finance`: são quatro linhas, e o ADR-022 rejeitou dependências que não
/// resolvem problema nenhum.
/// </summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
