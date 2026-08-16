using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Audit.Application;
using Rivo.Audit.Contracts;
using Rivo.Audit.Infrastructure.Persistence;

namespace Rivo.Audit.Infrastructure;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        // DbContext próprio, com histórico de migrações no schema do módulo:
        // `audit` evolui o seu schema sem tocar no de `identity`.
        services.AddDbContext<AuditDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AuditDbContext.Schema)
                    // Resiliencia de ligacao: a base de dados pode nao estar
                    // pronta no arranque (o depends_on do compose so vale no up,
                    // nao no restart), e em producao ha failover e reinicios.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<QueryAuditTrail>();

        // Cada módulo regista as policies das suas próprias permissões.
        // AddAuthorization é aditivo, por isso não colide com as de `identity`.
        services.AddAuthorization(options =>
        {
            foreach (var permission in AuditPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    /// <summary>Aplica as migrações do módulo. `audit` não tem seed: a trilha nasce vazia.</summary>
    public static async Task MigrateAuditModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<AuditDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}



