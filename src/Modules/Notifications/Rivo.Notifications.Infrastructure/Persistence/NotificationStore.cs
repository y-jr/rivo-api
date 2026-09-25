using Microsoft.EntityFrameworkCore;
using Rivo.Notifications.Application;
using Rivo.Notifications.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Notifications.Infrastructure.Persistence;

public sealed class NotificationStore(NotificationsDbContext context) : INotificationStore
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken) =>
        await context.Notifications.AddAsync(notification, cancellationToken);

    public async Task<Notification?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
        await context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, int? TotalCount)> ListForRecipientAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        PageRequest? pagina,
        CancellationToken cancellationToken)
    {
        var query = context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == recipientUserId);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        query = query.OrderByDescending(n => n.CreatedAt);

        // Sem página pedida, mantém o comportamento antigo de `limit` — quem
        // já consome esta rota sem `page`/`pageSize` não vê nada mudar.
        if (pagina is not { } p)
        {
            var itensDoLimite = await query.Take(limit).ToListAsync(cancellationToken);
            return (itensDoLimite, null);
        }

        var total = await query.CountAsync(cancellationToken);
        var itens = await query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).ToListAsync(cancellationToken);
        return (itens, total);
    }

    public async Task<IReadOnlyList<Notification>> ListUnreadForRecipientAsync(
        Guid recipientUserId,
        CancellationToken cancellationToken) =>
        // Rastreadas, ao contrário da listagem: quem as procura assim vai
        // alterá-las.
        await context.Notifications
            .Where(n => n.RecipientUserId == recipientUserId && n.ReadAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListDueForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken) =>
        // Com seguimento de alterações, ao contrário das leituras: o worker vai
        // alterar o estado destas entidades.
        await context.Notifications
            .Where(n => n.DeliveryStatus == NotificationDeliveryStatus.Pending
                        && n.NextAttemptAt != null
                        && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
