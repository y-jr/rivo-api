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

    /// <summary>
    /// Todas as não lidas de um destinatário, <strong>rastreadas</strong> e sem
    /// tecto.
    ///
    /// <para>
    /// Sem tecto de propósito, ao contrário da listagem: «marcar todas como
    /// lidas» que deixasse algumas por marcar seria pior do que não existir —
    /// quem carrega no botão fica a achar que ficou tudo tratado.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Notification>> ListUnreadForRecipientAsync(
        Guid recipientUserId,
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
