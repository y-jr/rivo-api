using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rivo.Finance.Application;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;
using Rivo.Finance.Infrastructure.Persistence;

namespace Rivo.Finance.Infrastructure;

public static class FinanceModuleExtensions
{
    public static IServiceCollection AddFinanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<FinanceDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", FinanceDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container
                    // e vive noutra maquina (ADR-029).
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.Configure<FinanceOptions>(configuration.GetSection(FinanceOptions.SectionName));

        services.AddScoped<ISalesInvoiceStore, SalesInvoiceStore>();

        services.TryAddSingletonTimeProvider();

        services.AddScoped<IssueSalesInvoice>();
        services.AddScoped<ListSalesInvoices>();
        services.AddScoped<GetSalesInvoice>();
        services.AddScoped<CancelSalesInvoice>();
        services.AddScoped<ListDocumentSeries>();
        services.AddScoped<OpenDocumentSeries>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in FinancePermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    /// <summary>
    /// A data de anulação vem do relógio injectado, não de
    /// <c>DateTimeOffset.UtcNow</c> espalhado pelo código — assim é fixável nos
    /// testes.
    /// </summary>
    private static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }

    public static async Task MigrateFinanceModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<FinanceDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Abre a série de numeração por omissão, se ainda não existir.
    ///
    /// <para>
    /// <strong>Idempotente</strong>, como todo o seed (ADR-016, ADR-028): se a
    /// série já existe, não lhe toca — e em particular <em>não</em> lhe recua o
    /// contador, que é o que tornaria isto perigoso.
    /// </para>
    ///
    /// <para>
    /// Existe porque sem ela um ambiente novo devolve <c>404</c> na primeira
    /// factura, e o passo esquecido só aparece quando alguém tenta facturar.
    /// </para>
    /// </summary>
    public static async Task SeedFinanceModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<FinanceOptions>>().Value;

        if (!options.SeedDefaultSeries || string.IsNullOrWhiteSpace(options.DefaultSeries))
        {
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var codigo = options.DefaultSeries.Trim().ToUpperInvariant();

        if (await context.Series.AnyAsync(
                s => s.Type == DocumentType.FT && s.Code == codigo, cancellationToken))
        {
            return;
        }

        await context.Series.AddAsync(DocumentSeries.Open(DocumentType.FT, codigo), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Rivo.Finance")
            .LogInformation("Série de numeração {Codigo} criada pelo seed.", codigo);
    }
}
