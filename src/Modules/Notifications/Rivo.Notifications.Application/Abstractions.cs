using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Application;

public interface INotificationStore
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken);

    Task<Notification?> FindAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Notificações com entrega externa em atraso, para o worker.</summary>
    Task<IReadOnlyList<Notification>> ListDueForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Canal de entrega externo.
///
/// <para>
/// Hoje existe uma implementação de desenvolvimento que regista em log. O
/// fornecedor de e-mail transaccional é decisão pendente — quando for tomada,
/// implementa-se esta interface e nada acima dela muda.
/// </para>
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Entrega. Lançar excepção sinaliza insucesso e faz agendar nova
    /// tentativa; não deve ser usada para recusas definitivas.
    /// </summary>
    Task DeliverAsync(Notification notification, CancellationToken cancellationToken);
}
