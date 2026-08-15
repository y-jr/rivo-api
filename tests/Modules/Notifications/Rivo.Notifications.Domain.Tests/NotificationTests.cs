using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Domain.Tests;

/// <summary>
/// Notificação — dois estados independentes: leitura na aplicação e entrega
/// externa. Separá-los evita que um problema no e-mail esconda a notificação
/// do destinatário.
/// </summary>
public class NotificationTests
{
    private static readonly Guid Recipient = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Notification Create(bool sendEmail = false) =>
        Notification.Create(Recipient, "identity.access_profile_assigned", "Perfil atribuído", "Detalhe.", sendEmail, Now);

    // --- Criação ----------------------------------------------------------

    [Fact]
    public void Create_WithoutRecipient_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Notification.Create(Guid.Empty, "tipo", "Título", "Mensagem", false, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutType_Throws(string type)
    {
        Assert.Throws<ArgumentException>(
            () => Notification.Create(Recipient, type, "Título", "Mensagem", false, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutTitle_Throws(string title)
    {
        Assert.Throws<ArgumentException>(
            () => Notification.Create(Recipient, "tipo", title, "Mensagem", false, Now));
    }

    /// <summary>
    /// O corpo é opcional — o título pode bastar. Recusar por falta de corpo
    /// impediria notificações legítimas de uma linha.
    /// </summary>
    [Fact]
    public void Create_WithoutMessage_IsAllowed()
    {
        var notification = Notification.Create(Recipient, "tipo", "Título", null!, false, Now);

        Assert.Equal(string.Empty, notification.Message);
    }

    /// <summary>
    /// Sem canal externo pedido não há nada a entregar: nasce concluída em vez
    /// de ficar eternamente pendente à espera de um worker que nunca terá o
    /// que fazer.
    /// </summary>
    [Fact]
    public void Create_WithoutExternalChannel_NeedsNoDelivery()
    {
        var notification = Create(sendEmail: false);

        Assert.Equal(NotificationDeliveryStatus.NotRequired, notification.DeliveryStatus);
        Assert.Null(notification.NextAttemptAt);
        Assert.False(notification.IsDueAt(Now));
    }

    [Fact]
    public void Create_WithExternalChannel_IsDueImmediately()
    {
        var notification = Create(sendEmail: true);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.Equal(Now, notification.NextAttemptAt);
        Assert.True(notification.IsDueAt(Now));
    }

    // --- Propriedade ------------------------------------------------------

    /// <summary>
    /// Invariante de propriedade do agregado, verificada no domínio e não por
    /// permissão (ADR-014): o que limita o acesso é <em>ser o destinatário</em>,
    /// e isso não é política configurável.
    /// </summary>
    [Fact]
    public void BelongsTo_OnlyTheRecipient()
    {
        var notification = Create();

        Assert.True(notification.BelongsTo(Recipient));
        Assert.False(notification.BelongsTo(Guid.CreateVersion7()));
    }

    // --- Leitura ----------------------------------------------------------

    [Fact]
    public void MarkAsRead_KeepsTheInstantOfTheFirstRead()
    {
        var notification = Create();
        var first = Now.AddMinutes(5);

        notification.MarkAsRead(first);
        notification.MarkAsRead(Now.AddHours(3));

        Assert.Equal(first, notification.ReadAt);
    }

    /// <summary>Uma notificação por entregar já é legível — os dois estados são independentes.</summary>
    [Fact]
    public void MarkAsRead_WorksWhileDeliveryIsStillPending()
    {
        var notification = Create(sendEmail: true);

        notification.MarkAsRead(Now.AddMinutes(1));

        Assert.NotNull(notification.ReadAt);
        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
    }

    // --- Entrega ----------------------------------------------------------

    /// <summary>
    /// Recuo exponencial: 2, 4, 8 e 16 minutos. Repetir de imediato costuma
    /// prolongar a avaria do serviço de destino em vez de a contornar.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void MarkFailed_BacksOffExponentially(int attempt, int expectedMinutes)
    {
        var notification = Create(sendEmail: true);

        for (var i = 0; i < attempt; i++)
        {
            notification.MarkFailed("SMTP indisponível", Now);
        }

        Assert.Equal(attempt, notification.DeliveryAttempts);
        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.Equal(Now.AddMinutes(expectedMinutes), notification.NextAttemptAt);
    }

    /// <summary>
    /// Ao quinto insucesso desiste-se. Fixado em teste porque o comentário XML
    /// de <c>MaxDeliveryAttempts</c> diz "ao sexto insucesso" e o código
    /// desiste ao quinto — <c>modules/notifications.md</c> confirma que o
    /// quinto é o comportamento pretendido. Este teste fixa o código; o
    /// comentário é que está errado.
    /// </summary>
    [Fact]
    public void MarkFailed_AtTheFifthFailure_IsAbandoned()
    {
        var notification = Create(sendEmail: true);

        for (var i = 0; i < Notification.MaxDeliveryAttempts; i++)
        {
            notification.MarkFailed("SMTP indisponível", Now);
        }

        Assert.Equal(NotificationDeliveryStatus.Abandoned, notification.DeliveryStatus);
        Assert.Null(notification.NextAttemptAt);
        Assert.False(notification.IsDueAt(Now.AddDays(1)));
    }

    /// <summary>Abandonada na entrega, continua legível na aplicação.</summary>
    [Fact]
    public void MarkFailed_Abandoned_RemainsReadable()
    {
        var notification = Create(sendEmail: true);

        for (var i = 0; i < Notification.MaxDeliveryAttempts; i++)
        {
            notification.MarkFailed("SMTP indisponível", Now);
        }

        notification.MarkAsRead(Now.AddDays(1));

        Assert.NotNull(notification.ReadAt);
    }

    [Fact]
    public void MarkFailed_TruncatesLongErrors()
    {
        var notification = Create(sendEmail: true);

        notification.MarkFailed(new string('x', 900), Now);

        Assert.Equal(500, notification.LastDeliveryError!.Length);
    }

    [Fact]
    public void MarkDelivered_ClearsTheRetryState()
    {
        var notification = Create(sendEmail: true);
        notification.MarkFailed("SMTP indisponível", Now);

        notification.MarkDelivered(Now.AddMinutes(3));

        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
        Assert.Equal(Now.AddMinutes(3), notification.DeliveredAt);
        Assert.Null(notification.NextAttemptAt);
        Assert.Null(notification.LastDeliveryError);
    }

    [Fact]
    public void IsDueAt_BeforeTheScheduledRetry_IsFalse()
    {
        var notification = Create(sendEmail: true);
        notification.MarkFailed("SMTP indisponível", Now);

        Assert.False(notification.IsDueAt(Now.AddMinutes(1)));
        Assert.True(notification.IsDueAt(Now.AddMinutes(2)));
    }
}
