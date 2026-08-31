using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Fiscal.Application;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Contracts;
using Rivo.Fiscal.Infrastructure.Persistence;

namespace Rivo.Fiscal.Infrastructure;

public static class FiscalModuleExtensions
{
    public static IServiceCollection AddFiscalModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<FiscalDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", FiscalDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ITaxRateStore, TaxRateStore>();

        // O contrato publicado. `commercial` e `finance` perguntam por aqui.
        services.AddScoped<ITaxDetermination, TaxDeterminationService>();

        services.AddScoped<ListTaxRates>();
        services.AddScoped<OpenTaxRateSchedule>();
        services.AddScoped<IntroduceTaxRate>();

        services.AddScoped<IIncomeTaxScheduleStore, IncomeTaxScheduleStore>();

        // O contrato publicado de IRT. `payroll` pergunta por aqui.
        services.AddScoped<IIncomeTaxDetermination, IncomeTaxDeterminationService>();

        services.AddScoped<GetIncomeTaxSchedule>();
        services.AddScoped<IntroduceIncomeTaxScheduleVersion>();

        services.AddScoped<ISubsidyExemptionStore, SubsidyExemptionStore>();

        // O contrato publicado de limiares de subsídio. `payroll` pergunta
        // por aqui.
        services.AddScoped<ISubsidyExemptionDetermination, SubsidyExemptionDeterminationService>();

        services.AddScoped<GetSubsidyExemptionSchedule>();
        services.AddScoped<IntroduceSubsidyExemptionVersion>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in FiscalPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateFiscalModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<FiscalDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
