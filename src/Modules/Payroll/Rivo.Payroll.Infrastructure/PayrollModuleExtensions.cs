using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Application.UseCases;
using Rivo.Payroll.Contracts;
using Rivo.Payroll.Infrastructure.Persistence;

namespace Rivo.Payroll.Infrastructure;

/// <summary>
/// Esqueleto do módulo `payroll` — ver `modules/payroll.md`. Folha e itens
/// com salário bruto, ligados a `approval` (via composition root — ver
/// <c>Rivo.Api.Composition.PayrollApprovalSubmission</c>). Sem cálculo de
/// IRT/INSS: os campos existem no modelo, ficam sempre nulos.
/// </summary>
public static class PayrollModuleExtensions
{
    public static IServiceCollection AddPayrollModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<PayrollDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", PayrollDbContext.Schema)
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IPayrollRunStore, PayrollRunStore>();

        services.AddScoped<ListPayrollRuns>();
        services.AddScoped<GetPayrollRun>();
        services.AddScoped<OpenPayrollRun>();
        services.AddScoped<AddPayrollItem>();
        services.AddScoped<SubmitPayrollRun>();
        services.AddScoped<ApplyPayrollDecision>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in PayrollPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigratePayrollModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<PayrollDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
