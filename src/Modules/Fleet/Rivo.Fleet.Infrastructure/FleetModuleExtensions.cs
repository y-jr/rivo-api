using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Contracts;
using Rivo.Fleet.Infrastructure.Persistence;

namespace Rivo.Fleet.Infrastructure;

/// <summary>
/// Composição do módulo `fleet` — ver `modules/fleet.md`. Manutenção e
/// Atribuição têm regra de negócio própria desde 2026-08-30; Plano de
/// Manutenção, Registo de Viagem, Despesa de Frota e Seguros continuam por
/// fazer.
/// </summary>
public static class FleetModuleExtensions
{
    public static IServiceCollection AddFleetModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<FleetDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", FleetDbContext.Schema)
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IVehicleStore, VehicleStore>();

        services.AddScoped<ListVehicles>();
        services.AddScoped<GetVehicle>();
        services.AddScoped<RegisterVehicle>();
        services.AddScoped<DeactivateVehicle>();
        services.AddScoped<OpenMaintenance>();
        services.AddScoped<CloseMaintenance>();
        services.AddScoped<AssignVehicle>();
        services.AddScoped<EndVehicleAssignment>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in FleetPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateFleetModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<FleetDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
