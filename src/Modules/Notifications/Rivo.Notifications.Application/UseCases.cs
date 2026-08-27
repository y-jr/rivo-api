using Rivo.Notifications.Contracts;
using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Application;

/// <summary>
/// Implementa o contrato publicado: enfileira, não entrega.
///
/// A gravação usa o <c>DbContext</c> de `notifications`, logo corre em
/// transacção própria — separada da do módulo que a pediu. É isto que corrige
/// o anti-padrão do protótipo, onde as notificações eram inseridas dentro da
/// transacção de negócio.
/// </summary>
public sealed class Notifier(INotificationStore store, TimeProvider clock) : INotifier
{
    public async Task QueueAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(
            request.RecipientUserId,
            request.Type,
            request.Title,
            request.Message,
            request.SendEmail,
            clock.GetUtcNow());

        await store.AddAsync(notification, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ListMyNotifications(INotificationStore store)
{
    private const int MaxLimit = 100;

    public async Task<IReadOnlyList<NotificationView>> ExecuteAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        var notifications = await store.ListForRecipientAsync(
            recipientUserId, unreadOnly, Math.Clamp(limit, 1, MaxLimit), cancellationToken);

        return [.. notifications.Select(n => new NotificationView(
            n.Id, n.Type, n.Title, n.Message, n.ReadAt is not null, n.CreatedAt, n.ReadAt))];
    }
}

public sealed record NotificationView(
    Guid NotificationId,
    string Type,
    string Title,
    string Message,
    bool Read,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>
/// Marca uma notificação como lida.
///
/// A verificação de propriedade acontece aqui, no domínio: só o destinatário
/// marca as suas. Devolver o mesmo resultado para "não existe" e "não é tua"
/// evita revelar a existência de notificações alheias.
/// </summary>
public sealed class MarkNotificationAsRead(INotificationStore store, TimeProvider clock)
{
    public async Task<bool> ExecuteAsync(
        Guid notificationId,
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        var notification = await store.FindAsync(notificationId, cancellationToken);

        if (notification is null || !notification.BelongsTo(recipientUserId))
        {
            return false;
        }

        notification.MarkAsRead(clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);

        return true;
    }
}

/// <summary>
/// Marca todas as não lidas do utilizador como lidas.
///
/// <para>
/// É o primeiro pedido que um cliente faz depois de mostrar um contador de não
/// lidas — e sem esta rota, marcar cinquenta significava cinquenta chamadas.
/// </para>
///
/// <para>
/// <strong>Só as do próprio.</strong> O identificador vem do token e nunca do
/// pedido, como no resto do módulo: se viesse do pedido, qualquer pessoa
/// limpava as notificações de outra.
/// </para>
/// </summary>
public sealed class MarkAllNotificationsAsRead(INotificationStore store, TimeProvider clock)
{
    /// <returns>Quantas ficaram marcadas. Zero é resultado normal, não erro.</returns>
    public async Task<int> ExecuteAsync(Guid recipientUserId, CancellationToken cancellationToken)
    {
        var porLer = await store.ListUnreadForRecipientAsync(recipientUserId, cancellationToken);

        if (porLer.Count == 0)
        {
            // Sem nada para marcar não se grava: uma gravação vazia incrementa
            // contadores de concorrência sem que nada tenha mudado.
            return 0;
        }

        var agora = clock.GetUtcNow();

        foreach (var notificacao in porLer)
        {
            notificacao.MarkAsRead(agora);
        }

        await store.SaveChangesAsync(cancellationToken);

        return porLer.Count;
    }
}

/// <summary>
/// Processa um lote de entregas em atraso.
///
/// Chamado pelo worker de segundo plano. Isolado dele para poder ser testado
/// sem levantar um <c>BackgroundService</c>.
/// </summary>
public sealed class DispatchPendingNotifications(
    INotificationStore store,
    INotificationChannel channel,
    TimeProvider clock)
{
    public async Task<DispatchOutcome> ExecuteAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = await store.ListDueForDeliveryAsync(now, batchSize, cancellationToken);

        var delivered = 0;
        var failed = 0;

        foreach (var notification in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await channel.DeliverAsync(notification, cancellationToken);
                notification.MarkDelivered(clock.GetUtcNow());
                delivered++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Uma entrega falhada não pode derrubar o lote: as restantes
                // notificações não têm culpa, e o recuo exponencial trata de
                // reagendar esta.
                notification.MarkFailed(exception.Message, clock.GetUtcNow());
                failed++;
            }
        }

        if (due.Count > 0)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return new DispatchOutcome(delivered, failed);
    }
}

public sealed record DispatchOutcome(int Delivered, int Failed);
