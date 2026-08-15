using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Notifications.Application;
using Rivo.Notifications.Contracts;
using Rivo.Notifications.Infrastructure.Delivery;
using Rivo.Notifications.Infrastructure.Persistence;

namespace Rivo.Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<NotificationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.Schema)
                    // Resiliencia de ligacao: a base de dados pode nao estar
                    // pronta no arranque (o depends_on do compose so vale no up,
                    // nao no restart), e em producao ha failover e reinicios.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services
            .AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName));

        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<ListMyNotifications>();
        services.AddScoped<MarkNotificationAsRead>();
        services.AddScoped<DispatchPendingNotifications>();

        services.AddSingleton<INotificationChannel, LoggingNotificationChannel>();
        services.AddHostedService<NotificationDeliveryWorker>();

        // Sem policies: ler notificações não exige permissão, exige ser o
        // destinatário. Essa verificação é invariante do domínio, não política
        // de autorização (ADR-014).

        return services;
    }

    /// <summary>Aplica as migrações. Sem seed: notificações são geradas em uso.</summary>
    public static async Task InitialiseNotificationsModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}



