using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        /*
         * A identidade da empresa, para o cabecalho do SAF-T (ADR-058).
         *
         * `ValidateOnStart` e a razao de isto ser opcoes e nao entidade: um
         * `.env` incompleto rebenta ao levantar a aplicacao, com o nome do
         * campo em falta. Uma tabela vazia rebentaria a meio de uma
         * exportacao, meses depois, no dia em que alguem precisasse do
         * ficheiro.
         */
        services.AddOptions<CompanyOptions>()
            .Bind(configuration.GetSection(CompanyOptions.SectionName))
            .ValidateOnStart();

        /*
         * A validacao vive num `IValidateOptions` e nao num `.Validate(pred,
         * mensagem)` porque a mensagem tem de **dizer o que esta errado**.
         *
         * O `Validate` com mensagem fixa so aceita uma cadeia constante, e
         * essa cadeia dizia "sem Company:Name e Company:TaxRegistrationNumber
         * nao ha como exportar" -- que passou a ser mentira quando a
         * verificacao ganhou o comprimento do NIF: alguem com nove digitos
         * preencheu os dois campos e leria que os nao tinha preenchido.
         *
         * Uma mensagem de arranque que aponta para o campo errado e pior do
         * que nenhuma: manda procurar onde nao esta.
         */
        services.AddSingleton<IValidateOptions<CompanyOptions>, ValidarCompanyOptions>();

        services.AddScoped(sp =>
            sp.GetRequiredService<IOptions<CompanyOptions>>().Value);

        services.AddScoped<ExportSaftFile>();

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
