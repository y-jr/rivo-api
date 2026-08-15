using Rivo.Audit.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Notifications.Contracts;

namespace Rivo.Identity.Application.UseCases;

/// <summary>Lista os Perfis de Acesso definidos e as permissões de cada um.</summary>
public sealed class ListAccessProfiles
{
    // O catálogo é a fonte de verdade e vive em código; não se lê da base de
    // dados, que guarda apenas as atribuições.
    public IReadOnlyList<AccessProfileView> Execute() =>
        [.. AccessProfiles.Catalogue.Select(entry => new AccessProfileView(entry.Key, entry.Value))];
}

public sealed record AccessProfileView(string Name, IReadOnlyList<string> Permissions);

/// <summary>Lista as contas existentes, para que um administrador possa atribuir perfis.</summary>
public sealed class ListUsers(IUserAccounts accounts)
{
    public Task<IReadOnlyList<UserSummary>> ExecuteAsync(CancellationToken cancellationToken) =>
        accounts.ListAsync(cancellationToken);
}

/// <summary>Atribui um Perfil de Acesso a um utilizador.</summary>
public sealed class AssignAccessProfile(IUserAccounts accounts, IAuditTrail audit, INotifier notifier)
{
    public async Task<AssignProfileOutcome> ExecuteAsync(
        Guid userId,
        string profile,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var outcome = await accounts.AssignProfileAsync(userId, profile, cancellationToken);

        if (outcome is not AssignProfileOutcome.Assigned)
        {
            return outcome;
        }

        // Auditada por BR-13: atribuir um perfil altera o que alguém pode
        // fazer, e é a operação mais sensível deste módulo.
        await audit.RecordAsync(
            new AuditRecord(
                AuditActions.ProfileAssigned,
                AuditEntityTypes.User,
                userId.ToString(),
                context,
                NewValue: $$"""{"profile":"{{profile}}"}"""),
            cancellationToken);

        // O acesso da pessoa mudou; faz sentido que saiba. Enfileira e segue —
        // a entrega acontece noutro momento, e uma falha nela nunca desfaz a
        // atribuição que acabou de ser gravada.
        await notifier.QueueAsync(
            new NotificationRequest(
                RecipientUserId: userId,
                Type: NotificationTypes.AccessProfileAssigned,
                Title: "Perfil de acesso atribuído",
                Message: $"Foi-lhe atribuído o perfil de acesso '{profile}'."),
            cancellationToken);

        return outcome;
    }
}
