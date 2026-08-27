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
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container e
                    // vive noutra maquina. Falhas de rede transitorias sao normais,
                    // nao excepcionais — e o arranque pode apanhar a base indisponivel.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services
            .AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName));

        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<ListMyNotifications>();
        services.AddScoped<MarkNotificationAsRead>();
        services.AddScoped<MarkAllNotificationsAsRead>();
        services.AddScoped<DispatchPendingNotifications>();

        services.AddSingleton<INotificationChannel, LoggingNotificationChannel>();
        services.AddHostedService<NotificationDeliveryWorker>();

        // Sem policies: ler notificações não exige permissão, exige ser o
        // destinatário. Essa verificação é invariante do domínio, não política
        // de autorização (ADR-014).

        return services;
    }

    /// <summary>Aplica as migrações. Sem seed: notificações são geradas em uso.</summary>
    public static async Task MigrateNotificationsModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}



