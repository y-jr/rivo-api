namespace Rivo.Notifications.Domain;

/// <summary>
/// Uma notificação dirigida a um utilizador.
///
/// <para>
/// Tem dois estados independentes, e é importante não os confundir:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <strong>Leitura</strong> (<see cref="ReadAt"/>) — estado na aplicação.
///     A notificação existe e é visível assim que é criada.
///   </description></item>
///   <item><description>
///     <strong>Entrega externa</strong> (<see cref="DeliveryStatus"/>) — envio
///     por e-mail ou outro canal, que corre em segundo plano e pode falhar e
///     ser repetido.
///   </description></item>
/// </list>
///
/// <para>
/// Uma notificação por entregar já é legível. Separá-las evita que um
/// problema no e-mail esconda a notificação do destinatário.
/// </para>
/// </summary>
public sealed class Notification
{
    /// <summary>
    /// Fim das tentativas. Ao quinto insucesso, o problema não é transitório —
    /// insistir só enche a fila e mascara a avaria.
    /// </summary>
    public const int MaxDeliveryAttempts = 5;

    private Notification()
    {
        Type = string.Empty;
        Title = string.Empty;
        Message = string.Empty;
    }

    private Notification(
        Guid id,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? actionUrl,
        string? actionLabel,
        NotificationDeliveryStatus deliveryStatus,
        DateTimeOffset createdAt)
    {
        Id = id;
        RecipientUserId = recipientUserId;
        Type = type;
        Title = title;
        Message = message;
        ActionUrl = actionUrl;
        ActionLabel = actionLabel;
        DeliveryStatus = deliveryStatus;
        NextAttemptAt = deliveryStatus is NotificationDeliveryStatus.Pending ? createdAt : null;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Contador de concorrência optimista (ADR-002, ADR-025).
    ///
    /// <para>
    /// É aqui que mais interessa de todo o Rivo hoje: o worker de entrega e o
    /// destinatário tocam na mesma linha ao mesmo tempo — um a marcar a entrega,
    /// o outro a marcar como lida. Sem isto, a última escrita a chegar apagava
    /// silenciosamente a outra.
    /// </para>
    ///
    /// <para>
    /// Incrementado pela infraestrutura ao gravar, nunca pelo domínio. O
    /// <c>private set</c> existe só para o EF Core o materializar.
    /// </para>
    /// </summary>
    public int Version { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public string Type { get; private set; }

    public string Title { get; private set; }

    public string Message { get; private set; }

    /// <summary>
    /// Para onde leva a notificação, quando leva a algum lado.
    ///
    /// <para>
    /// <strong>É dado, e não marcação.</strong> O <see cref="Message"/> continua
    /// texto simples porque é o mesmo corpo que a aplicação mostra na lista de
    /// notificações; enfiar-lhe HTML punha etiquetas à vista nesse ecrã. Guardar
    /// o destino à parte deixa cada canal decidir como o apresenta — o correio
    /// desenha um botão, a aplicação pode desenhar uma ligação — sem que o
    /// módulo saiba nada de nenhum dos dois.
    /// </para>
    /// </summary>
    public string? ActionUrl { get; private set; }

    /// <summary>O que o botão diz. Sem isto o destino não tem nome.</summary>
    public string? ActionLabel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public NotificationDeliveryStatus DeliveryStatus { get; private set; }

    public int DeliveryAttempts { get; private set; }

    /// <summary>Nulo quando não há entrega externa pendente.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? LastDeliveryError { get; private set; }

    public static Notification Create(
        Guid recipientUserId,
        string type,
        string title,
        string message,
        bool sendEmail,
        DateTimeOffset now,
        string? actionUrl = null,
        string? actionLabel = null)
    {
        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("A notificação tem de ter destinatário.", nameof(recipientUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        // Um destino sem nome deixa um botão por legendar; um nome sem destino
        // deixa um botão que não vai a lado nenhum. Qualquer um dos dois só se
        // descobre na caixa de correio de quem recebe, que é tarde.
        var temUrl = !string.IsNullOrWhiteSpace(actionUrl);
        var temEtiqueta = !string.IsNullOrWhiteSpace(actionLabel);

        if (temUrl != temEtiqueta)
        {
            throw new ArgumentException(
                "A acção de uma notificação é o par destino+etiqueta: ou vêm os dois, ou nenhum.",
                temUrl ? nameof(actionLabel) : nameof(actionUrl));
        }

        // Absoluto e http(s): o destino vai para dentro de uma mensagem de
        // correio, onde um caminho relativo não tem a que se referir.
        if (temUrl && (!Uri.TryCreate(actionUrl, UriKind.Absolute, out var destino)
            || (destino.Scheme != Uri.UriSchemeHttp && destino.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException(
                $"O destino da acção tem de ser um URL absoluto http(s): '{actionUrl}'.",
                nameof(actionUrl));
        }

        return new Notification(
            Guid.CreateVersion7(),
            recipientUserId,
            type.Trim(),
            title.Trim(),
            message ?? string.Empty,
            temUrl ? actionUrl!.Trim() : null,
            temEtiqueta ? actionLabel!.Trim() : null,
            // Sem canal externo não há nada a entregar: nasce concluída em vez
            // de ficar pendente para sempre à espera de um worker.
            sendEmail ? NotificationDeliveryStatus.Pending : NotificationDeliveryStatus.NotRequired,
            now);
    }

    /// <summary>
    /// Marca como lida. Idempotente: manter o instante da primeira leitura é o
    /// que interessa a quem depois quiser saber quando foi vista.
    /// </summary>
    public void MarkAsRead(DateTimeOffset now) => ReadAt ??= now;

    /// <summary>
    /// Só o destinatário vê e marca as suas notificações.
    ///
    /// É invariante de propriedade do agregado, verificada no domínio — não é
    /// política configurável, e por isso não entra no modelo de permissões
    /// (ADR-014).
    /// </summary>
    public bool BelongsTo(Guid userId) => RecipientUserId == userId;

    public void MarkDelivered(DateTimeOffset now)
    {
        DeliveryStatus = NotificationDeliveryStatus.Delivered;
        DeliveredAt = now;
        NextAttemptAt = null;
        LastDeliveryError = null;
    }

    /// <summary>
    /// Regista um insucesso e agenda nova tentativa com recuo exponencial.
    ///
    /// O recuo evita martelar um serviço já em dificuldade — repetir de
    /// imediato costuma prolongar a avaria em vez de a contornar.
    /// </summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        DeliveryAttempts++;
        LastDeliveryError = Truncate(error);

        if (DeliveryAttempts >= MaxDeliveryAttempts)
        {
            DeliveryStatus = NotificationDeliveryStatus.Abandoned;
            NextAttemptAt = null;
            return;
        }

        // 2, 4, 8, 16 minutos.
        NextAttemptAt = now.AddMinutes(Math.Pow(2, DeliveryAttempts));
    }

    public bool IsDueAt(DateTimeOffset instant) =>
        DeliveryStatus is NotificationDeliveryStatus.Pending
        && NextAttemptAt is not null
        && NextAttemptAt <= instant;

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];
}

public enum NotificationDeliveryStatus
{
    /// <summary>Não foi pedido canal externo. Só existe na aplicação.</summary>
    NotRequired,

    /// <summary>À espera de entrega, ou de nova tentativa.</summary>
    Pending,

    Delivered,

    /// <summary>Esgotou as tentativas. Continua legível na aplicação.</summary>
    Abandoned,
}
