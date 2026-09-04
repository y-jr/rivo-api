using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Inventory.Application;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Application.UseCases;
using Rivo.Inventory.Contracts;
using Rivo.Inventory.Infrastructure.Persistence;

namespace Rivo.Inventory.Infrastructure;

/// <summary>
/// Composição do módulo `inventory` — ver `modules/inventory.md`. Movimento
/// tem regra de negócio própria desde 2026-08-30; Armazém, Transferência,
/// Contagem e Valorização (custo médio ponderado) desde 2026-08-31.
/// </summary>
public static class InventoryModuleExtensions
{
    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<InventoryDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.Schema)
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IInventoryItemStore, InventoryItemStore>();
        services.AddScoped<IWarehouseStore, WarehouseStore>();
        services.AddScoped<IInventoryCountStore, InventoryCountStore>();

        services.AddScoped<ListInventoryItems>();
        services.AddScoped<GetInventoryItem>();
        services.AddScoped<RegisterInventoryItem>();
        services.AddScoped<SetInventoryItemStatus>();
        services.AddScoped<RegisterReceipt>();
        services.AddScoped<RegisterIssue>();
        services.AddScoped<RegisterAdjustment>();
        services.AddScoped<TransferStock>();
        services.AddScoped<ListWarehouses>();
        services.AddScoped<GetWarehouse>();
        services.AddScoped<RegisterWarehouse>();
        services.AddScoped<SetWarehouseStatus>();
        services.AddScoped<ListInventoryCounts>();
        services.AddScoped<GetInventoryCount>();
        services.AddScoped<OpenInventoryCount>();
        services.AddScoped<AddInventoryCountLine>();
        services.AddScoped<CloseInventoryCount>();
        services.AddScoped<CancelInventoryCount>();
        services.AddScoped<GetStockValuationByPeriod>();

        // O contrato publicado — primeiro consumidor, Analytics & IA
        // (módulo 10).
        services.AddScoped<IInventoryValuationOverview, InventoryValuationOverview>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in InventoryPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateInventoryModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<InventoryDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
