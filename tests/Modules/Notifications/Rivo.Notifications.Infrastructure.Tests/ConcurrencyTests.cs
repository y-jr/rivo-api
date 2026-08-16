using Microsoft.EntityFrameworkCore;
using Rivo.Notifications.Domain;
using Rivo.Notifications.Infrastructure.Persistence;
using Rivo.TestSupport;

namespace Rivo.Notifications.Infrastructure.Tests;

/// <summary>
/// Concorrência optimista contra um PostgreSQL real (ADR-025, ADR-026).
///
/// <para>
/// <strong>Fecha a lacuna que o ADR-025 deixou aberta.</strong> Quando a coluna
/// `version` foi implementada, o mecanismo só foi provado por SQL escrito à
/// mão — dois `UPDATE` com a mesma versão de partida dando `UPDATE 1` e
/// `UPDATE 0`. Isso mostra que o PostgreSQL faz a sua parte; não mostra que o
/// EF Core está configurado para tirar partido dela. É o que se verifica aqui.
/// </para>
///
/// <para>
/// `notifications` é o sítio certo para o fazer: é onde a contenção é real
/// hoje. O worker de entrega e o destinatário tocam na mesma linha ao mesmo
/// tempo — um a marcar a entrega, o outro a marcar como lida.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConcurrencyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private NotificationsDbContext Context() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task InitializeAsync()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Duas escritas concorrentes sobre a mesma notificação: a segunda falha
    /// em vez de sobrepor.
    ///
    /// <para>
    /// É o cenário exacto do worker contra o destinatário. Sem o token de
    /// concorrência, a segunda gravação apagava a primeira em silêncio — e o
    /// sintoma seria uma notificação que aparece por ler depois de ter sido
    /// lida, ou uma entrega perdida, sem erro em lado nenhum.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ConcurrentWrites_TheSecondFailsInsteadOfOverwriting()
    {
        var id = await GivenANotification(sendEmail: true);

        // Dois contextos distintos: é o que representa dois processos a ler a
        // mesma linha antes de qualquer um gravar.
        await using var primeiro = Context();
        await using var segundo = Context();

        var doDestinatario = await primeiro.Notifications.SingleAsync(n => n.Id == id);
        var doWorker = await segundo.Notifications.SingleAsync(n => n.Id == id);

        doDestinatario.MarkAsRead(DateTimeOffset.UtcNow);
        await primeiro.SaveChangesAsync();

        doWorker.MarkDelivered(DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
    }

    /// <summary>
    /// A escrita que ganhou permanece. Complementa o teste acima: não basta a
    /// segunda falhar — a primeira tem de ter ficado gravada.
    /// </summary>
    [Fact]
    public async Task WhenAWriteIsRejected_TheWinningWriteSurvives()
    {
        var id = await GivenANotification(sendEmail: true);
        var lidaEm = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        await using (var primeiro = Context())
        await using (var segundo = Context())
        {
            var a = await primeiro.Notifications.SingleAsync(n => n.Id == id);
            var b = await segundo.Notifications.SingleAsync(n => n.Id == id);

            a.MarkAsRead(lidaEm);
            await primeiro.SaveChangesAsync();

            b.MarkDelivered(DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
        }

        await using var verificacao = Context();
        var actual = await verificacao.Notifications.SingleAsync(n => n.Id == id);

        Assert.Equal(lidaEm, actual.ReadAt);

        // A entrega perdeu a corrida, e por isso não aconteceu. O worker volta
        // a tentar — que é o comportamento correcto.
        Assert.Equal(NotificationDeliveryStatus.Pending, actual.DeliveryStatus);
    }

    /// <summary>
    /// O contador sobe a cada gravação, sem o domínio lhe tocar.
    ///
    /// <para>
    /// É o que faz a detecção funcionar. Se o incremento deixasse de
    /// acontecer — alguém a remover o override de `SaveChangesAsync`, por
    /// exemplo — a coluna ficava a zero para sempre e a protecção desaparecia
    /// sem nada falhar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Version_IncrementsOnEveryWrite()
    {
        var id = await GivenANotification(sendEmail: false);

        await using var db = Context();
        var notificacao = await db.Notifications.SingleAsync(n => n.Id == id);

        Assert.Equal(0, notificacao.Version);

        notificacao.MarkAsRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(1, notificacao.Version);
    }

    /// <summary>
    /// Escritas sucessivas sobre a mesma linha, sem concorrência, não falham.
    ///
    /// <para>
    /// Guarda contra o excesso de zelo: um token mal configurado torna
    /// qualquer segunda gravação impossível, e o sintoma seria o sistema a
    /// recusar operações legítimas.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SequentialWrites_AreNotMistakenForAConflict()
    {
        var id = await GivenANotification(sendEmail: true);

        await using var db = Context();
        var notificacao = await db.Notifications.SingleAsync(n => n.Id == id);

        notificacao.MarkFailed("SMTP indisponível", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        notificacao.MarkDelivered(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(2, notificacao.Version);
        Assert.Equal(NotificationDeliveryStatus.Delivered, notificacao.DeliveryStatus);
    }

    private async Task<Guid> GivenANotification(bool sendEmail)
    {
        await using var db = Context();

        var notificacao = Notification.Create(
            Guid.CreateVersion7(),
            "teste.concorrencia",
            "Título",
            "Mensagem",
            sendEmail,
            DateTimeOffset.UtcNow);

        db.Notifications.Add(notificacao);
        await db.SaveChangesAsync();

        return notificacao.Id;
    }
}
