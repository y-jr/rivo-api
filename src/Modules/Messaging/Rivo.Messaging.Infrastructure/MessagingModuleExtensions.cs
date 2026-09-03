using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Messaging.Application;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Application.UseCases;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Infrastructure.Persistence;

namespace Rivo.Messaging.Infrastructure;

public static class MessagingModuleExtensions
{
    public static IServiceCollection AddMessagingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<MessagingDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", MessagingDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IConversationStore, ConversationStore>();

        services.AddScoped<SendCustomerMessage>();
        services.AddScoped<ListMyConversations>();
        services.AddScoped<SendEmployeeReply>();
        services.AddScoped<CloseConversation>();
        services.AddScoped<ListConversations>();
        services.AddScoped<GetConversation>();

        // O contrato publicado para composição (ADR-045) — único consumidor
        // previsto, o Portal do Cliente.
        services.AddScoped<ICustomerMessaging, CustomerMessaging>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in MessagingPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateMessagingModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<MessagingDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
