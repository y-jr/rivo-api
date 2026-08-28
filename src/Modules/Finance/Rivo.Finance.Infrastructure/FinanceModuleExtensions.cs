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

        services.AddScoped<IssueCreditNote>();
        services.AddScoped<CancelCreditNote>();
        services.AddScoped<ListCreditNotes>();
        services.AddScoped<GetCreditNote>();
        services.AddScoped<RegisterReceipt>();
        services.AddScoped<CancelReceipt>();
        services.AddScoped<ListReceipts>();
        services.AddScoped<GetReceipt>();
        services.AddScoped<GetInvoiceBalance>();

        // Contas a Pagar e Tesouraria.
        services.AddScoped<IPayablesStore, PayablesStore>();
        services.AddScoped<OpenBankAccount>();
        services.AddScoped<DepositToAccount>();
        services.AddScoped<WithdrawFromAccount>();
        services.AddScoped<SetBankAccountStatus>();
        services.AddScoped<ListBankAccounts>();
        services.AddScoped<GetAccountStatement>();
        services.AddScoped<RegisterPurchaseInvoice>();
        services.AddScoped<ListPurchaseInvoices>();
        services.AddScoped<GetPurchaseInvoice>();
        services.AddScoped<CreatePaymentRequest>();
        services.AddScoped<ListPaymentRequests>();
        services.AddScoped<GetPaymentRequest>();
        services.AddScoped<CancelPaymentRequest>();
        services.AddScoped<ExecutePayment>();

        // Contabilidade & Fecho.
        services.AddScoped<ILedgerStore, LedgerStore>();
        services.AddScoped<OpenLedgerAccount>();
        services.AddScoped<ListLedgerAccounts>();
        services.AddScoped<DeactivateLedgerAccount>();
        services.AddScoped<OpenJournal>();
        services.AddScoped<ListJournals>();
        services.AddScoped<PostJournalEntry>();
        services.AddScoped<ListJournalEntries>();
        services.AddScoped<GetJournalEntry>();
        services.AddScoped<VoidJournalEntry>();
        services.AddScoped<ManageAccountingPeriods>();
        services.AddScoped<GetTrialBalance>();
        services.AddScoped<PostDocument>();
        services.AddScoped<DefinePostingRule>();
        services.AddScoped<ListPostingRules>();
        services.AddScoped<DeactivatePostingRule>();

        // Planeamento.
        services.AddScoped<IPlanningStore, PlanningStore>();
        services.AddScoped<OpenCostCentre>();
        services.AddScoped<ListCostCentres>();
        services.AddScoped<DraftBudget>();
        services.AddScoped<ReviseBudget>();
        services.AddScoped<ApproveBudget>();
        services.AddScoped<ListBudgets>();
        services.AddScoped<RecordCostForecast>();
        services.AddScoped<ListCostForecasts>();

        // **O disponível orçamental de BR-8.** Registado como o contrato
        // publicado, para que `approval` o receba sem conhecer nada de
        // `finance` além dele.
        services.AddScoped<IBudgetAvailability, BudgetAvailability>();

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
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Rivo.Finance");

        // Uma serie por tipo de documento. Partilham o codigo porque a chave e
        // (tipo, codigo): `FT S001`, `NC S001` e `RG S001` sao tres series
        // distintas, cada uma com o seu contador.
        foreach (var tipo in new[] { DocumentType.FT, DocumentType.NC, DocumentType.RG })
        {
            if (await context.Series.AnyAsync(
                    s => s.Type == tipo && s.Code == codigo, cancellationToken))
            {
                continue;
            }

            await context.Series.AddAsync(DocumentSeries.Open(tipo, codigo), cancellationToken);
            logger.LogInformation("Série de numeração {Tipo} {Codigo} criada pelo seed.", tipo, codigo);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
