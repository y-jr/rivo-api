using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Fleet.Application;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Contracts;
using Rivo.Fleet.Infrastructure.Persistence;

namespace Rivo.Fleet.Infrastructure;

/// <summary>
/// Composição do módulo `fleet` — ver `modules/fleet.md`. Manutenção,
/// Atribuição e Plano de Manutenção têm regra de negócio própria desde
/// 2026-08-30; Registo de Viagem, Despesa de Frota e Seguros desde 2026-08-31.
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

        // O contrato publicado. `projects` pergunta por aqui (Alocação de
        // Recursos, 2026-08-31).
        services.AddScoped<IVehicleDirectory, VehicleDirectory>();

        // Segundo contrato publicado — despesa e distância por período,
        // primeiro consumidor Analytics & IA (módulo 10).
        services.AddScoped<IFleetActivityOverview, FleetActivityOverview>();

        services.AddScoped<ListVehicles>();
        services.AddScoped<GetVehicle>();
        services.AddScoped<RegisterVehicle>();
        services.AddScoped<DeactivateVehicle>();
        services.AddScoped<OpenMaintenance>();
        services.AddScoped<CloseMaintenance>();
        services.AddScoped<AssignVehicle>();
        services.AddScoped<EndVehicleAssignment>();
        services.AddScoped<SchedulePlan>();
        services.AddScoped<CompletePlanCycle>();
        services.AddScoped<CancelPlan>();
        services.AddScoped<ListDueMaintenancePlans>();
        services.AddScoped<RegisterTrip>();
        services.AddScoped<RegisterExpense>();
        services.AddScoped<AttachDocumentToVehicle>();
        services.AddScoped<ListVehicleDocuments>();

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
