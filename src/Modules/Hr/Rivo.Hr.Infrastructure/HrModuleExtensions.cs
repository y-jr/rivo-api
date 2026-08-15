using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Hr.Application;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;
using Rivo.Hr.Infrastructure.Persistence;

namespace Rivo.Hr.Infrastructure;

public static class HrModuleExtensions
{
    public static IServiceCollection AddHrModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<HrDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", HrDbContext.Schema)
                    // Resiliencia de ligacao: a base de dados pode nao estar
                    // pronta no arranque (o depends_on do compose so vale no up,
                    // nao no restart), e em producao ha failover e reinicios.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IHrStore, HrStore>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();

        services.AddScoped<ListEmployees>();
        services.AddScoped<HireEmployee>();
        services.AddScoped<ListDepartments>();
        services.AddScoped<CreateDepartment>();
        services.AddScoped<ListPositions>();
        services.AddScoped<CreatePosition>();
        services.AddScoped<AssignPosition>();
        services.AddScoped<AttachDocumentToEmployee>();
        services.AddScoped<ListEmployeeDocuments>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in HrPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    /// <summary>
    /// Aplica as migrações. `hr` não tem seed: colaboradores, departamentos e
    /// cargos são dados de negócio, e semeá-los seria inventar organização.
    /// </summary>
    public static async Task InitialiseHrModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<HrDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}




