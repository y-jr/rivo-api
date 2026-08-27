using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Procurement.Application;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Application.UseCases;
using Rivo.Procurement.Contracts;
using Rivo.Procurement.Infrastructure.Persistence;

namespace Rivo.Procurement.Infrastructure;

public static class ProcurementModuleExtensions
{
    public static IServiceCollection AddProcurementModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<ProcurementDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", ProcurementDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IProcurementStore, ProcurementStore>();

        // O contrato publicado. `finance` lê o fornecedor por aqui.
        services.AddScoped<ISupplierDirectory, SupplierDirectory>();

        services.AddScoped<ListSuppliers>();
        services.AddScoped<GetSupplier>();
        services.AddScoped<RegisterSupplier>();
        services.AddScoped<UpdateSupplier>();
        services.AddScoped<SetSupplierStatus>();

        services.AddScoped<ListRequisitions>();
        services.AddScoped<GetRequisition>();
        services.AddScoped<OpenRequisition>();
        services.AddScoped<SubmitRequisition>();
        services.AddScoped<ApplyRequisitionDecision>();
        services.AddScoped<CancelRequisition>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in ProcurementPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateProcurementModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ProcurementDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
