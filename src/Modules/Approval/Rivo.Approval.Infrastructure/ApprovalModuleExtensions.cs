using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Approval.Application;
using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Application.UseCases;
using Rivo.Approval.Contracts;
using Rivo.Approval.Infrastructure.Persistence;

namespace Rivo.Approval.Infrastructure;

public static class ApprovalModuleExtensions
{
    public static IServiceCollection AddApprovalModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<ApprovalDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", ApprovalDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IApprovalStore, ApprovalStore>();

        // O contrato publicado. É por aqui que `hr` submete, sem conhecer nada
        // de `approval` além de Rivo.Approval.Contracts (ADR-034).
        services.AddScoped<IApprovalGateway, ApprovalGateway>();

        services.AddScoped<ListApprovalPolicies>();
        services.AddScoped<CreateApprovalPolicy>();
        services.AddScoped<DeactivateApprovalPolicy>();
        services.AddScoped<GetApprovalRequestHistory>();
        services.AddScoped<ListApprovalRequests>();
        services.AddScoped<DecideOnRequest>();
        services.AddScoped<CancelRequest>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        // `AddAuthorization` é aditivo, por isso não colide com as dos outros.
        services.AddAuthorization(options =>
        {
            foreach (var permission in ApprovalPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateApprovalModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ApprovalDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
