using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Commercial.Application;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Application.UseCases;
using Rivo.Commercial.Contracts;
using Rivo.Commercial.Infrastructure.Persistence;

namespace Rivo.Commercial.Infrastructure;

public static class CommercialModuleExtensions
{
    public static IServiceCollection AddCommercialModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<CommercialDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", CommercialDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ICustomerStore, CustomerStore>();

        // O contrato publicado. `finance` lê o cliente por aqui para emitir.
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();

        services.AddScoped<ListCustomers>();
        services.AddScoped<GetCustomer>();
        services.AddScoped<RegisterCustomer>();
        services.AddScoped<UpdateCustomer>();
        services.AddScoped<SetCustomerStatus>();
        services.AddScoped<LinkCustomerAccount>();
        services.AddScoped<UnlinkCustomerAccount>();
        services.AddScoped<GetCustomerAccountHistory>();
        services.AddScoped<AssignCustomerOwner>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in CommercialPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateCommercialModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<CommercialDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
