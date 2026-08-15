using Microsoft.EntityFrameworkCore;
using Rivo.Notifications.Application;
using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Infrastructure.Persistence;

public sealed class NotificationStore(NotificationsDbContext context) : INotificationStore
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken) =>
        await context.Notifications.AddAsync(notification, cancellationToken);

    public async Task<Notification?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
        await context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == recipientUserId);

        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

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
