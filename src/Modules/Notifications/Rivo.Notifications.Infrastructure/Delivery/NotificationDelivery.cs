using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rivo.Notifications.Application;
using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Infrastructure.Delivery;

/// <summary>
/// Canal de desenvolvimento: regista em log em vez de enviar.
///
/// <para>
/// Existe para que o percurso de entrega — fila, worker, estado, recuo — seja
/// real e testável sem um fornecedor de e-mail. A escolha do fornecedor é
/// decisão pendente; quando for tomada, implementa-se
/// <see cref="INotificationChannel"/> e substitui-se este registo.
/// </para>
/// </summary>
public sealed class LoggingNotificationChannel(ILogger<LoggingNotificationChannel> logger) : INotificationChannel
{
    public Task DeliverAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Título e tipo bastam para confirmar a entrega. O conteúdo pode ser
        // sensível e não deve ir para os logs.
        logger.LogInformation(
            "Notificação {NotificationId} do tipo {Type} entregue a {RecipientUserId}: {Title}",
            notification.Id, notification.Type, notification.RecipientUserId, notification.Title);

        return Task.CompletedTask;
    }
}

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    /// <summary>Intervalo entre varreduras da fila.</summary>
    public int PollIntervalSeconds { get; init; } = 10;

    /// <summary>Notificações por ciclo. Limita a duração de cada varredura.</summary>
    public int BatchSize { get; init; } = 50;
}

/// <summary>
/// Worker que entrega as notificações pendentes.
///
/// <para>
/// É isto que torna a entrega assíncrona: o módulo de origem enfileira e
/// segue; a entrega acontece aqui, fora do pedido HTTP e fora da transacção
/// de negócio. Uma falha de envio nunca chega a quem pediu a notificação.
/// </para>
///
/// <para>
/// Sondagem à base de dados em vez de fila de mensagens: não justifica um
/// serviço novo à escala prevista, e a tabela já é a fila.
/// </para>
/// </summary>
public sealed class NotificationDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDeliveryOptions> options,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    private readonly NotificationDeliveryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        logger.LogInformation(
            "Entrega de notificações activa: intervalo {Interval}s, lote {BatchSize}.",
            _options.PollIntervalSeconds, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Scope por ciclo: o DbContext é scoped e não deve viver o
                // tempo todo do worker, que é singleton.
                using var scope = scopeFactory.CreateScope();

                var dispatch = scope.ServiceProvider.GetRequiredService<DispatchPendingNotifications>();
                var outcome = await dispatch.ExecuteAsync(_options.BatchSize, stoppingToken);

                if (outcome.Delivered > 0 || outcome.Failed > 0)
                {
                    logger.LogInformation(
                        "Ciclo de entrega: {Delivered} entregues, {Failed} falhadas.",
                        outcome.Delivered, outcome.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // O worker não pode morrer por um ciclo mau — se morresse,
                // deixaria de haver entregas até ao próximo arranque.
                logger.LogError(exception, "Ciclo de entrega de notificações falhou.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
