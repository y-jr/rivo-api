using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Hr.Application;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;
using Rivo.Hr.Infrastructure.Persistence;
using Rivo.Hr.Infrastructure.Reconciliation;

namespace Rivo.Hr.Infrastructure;

public static class HrModuleExtensions
{
    public static IServiceCollection AddHrModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<HrDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", HrDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container e
                    // vive noutra maquina. Falhas de rede transitorias sao normais,
                    // nao excepcionais — e o arranque pode apanhar a base indisponivel.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IHrStore, HrStore>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();

        services.AddScoped<ListEmployees>();
        services.AddScoped<HireEmployee>();
        services.AddScoped<LinkEmployeeAccount>();
        services.AddScoped<UnlinkEmployeeAccount>();
        services.AddScoped<ListDepartments>();
        services.AddScoped<CreateDepartment>();
        services.AddScoped<ListPositions>();
        services.AddScoped<CreatePosition>();
        services.AddScoped<AssignPosition>();
        services.AddScoped<ApplyPositionApprovalOutcome>();
        services.AddScoped<ReconcilePendingAssignments>();

        // Ferias: mesmo padrao de submissao a governanca (docs §1(d)).
        services.AddScoped<ListLeave>();
        services.AddScoped<RequestLeave>();
        services.AddScoped<CancelLeave>();
        services.AddScoped<ApplyLeaveApprovalOutcome>();
        services.AddScoped<ReconcilePendingLeave>();

        // Worker que aplica, em ciclo, as decisões de aprovação a atribuições
        // pendentes. Existe porque `approval` não pode empurrar a decisão — não
        // lhe é permitido modificar dados de negócio do módulo de origem — e
        // sem isto uma atribuição aprovada ficava pendente até alguém chamar o
        // endpoint à mão.
        services
            .AddOptions<PositionApprovalReconciliationOptions>()
            .Bind(configuration.GetSection(PositionApprovalReconciliationOptions.SectionName));

        services.AddHostedService<PositionApprovalReconciliationWorker>();
        services.AddScoped<AttachDocumentToEmployee>();
        services.AddScoped<ListEmployeeDocuments>();

        // Contrato de Trabalho e Assiduidade.
        services.AddScoped<ListEmploymentContracts>();
        services.AddScoped<DrawEmploymentContract>();
        services.AddScoped<TerminateEmploymentContract>();
        services.AddScoped<ListAttendance>();
        services.AddScoped<ClockAttendance>();
        services.AddScoped<RecordAbsence>();

        // Benefícios.
        services.AddScoped<ListBenefits>();
        services.AddScoped<CreateBenefit>();
        services.AddScoped<ListBenefitEnrolments>();
        services.AddScoped<EnrolInBenefit>();
        services.AddScoped<CancelBenefitEnrolment>();

        // Recrutamento.
        services.AddScoped<ListJobOpenings>();
        services.AddScoped<OpenJobOpening>();
        services.AddScoped<CloseJobOpening>();
        services.AddScoped<ListCandidates>();
        services.AddScoped<ApplyToJobOpening>();
        services.AddScoped<AdvanceCandidate>();
        services.AddScoped<HireCandidate>();

        // Onboarding e offboarding.
        services.AddScoped<ListLifecycleProcesses>();
        services.AddScoped<StartLifecycleProcess>();
        services.AddScoped<CompleteLifecycleTask>();
        services.AddScoped<CompleteLifecycleProcess>();

        // Cada módulo regista as policies das suas permissões (ADR-014).
        services.AddAuthorization(options =>
        {
            foreach (var permission in HrPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    /// <summary>
    /// Aplica as migrações. `hr` não tem seed: colaboradores, departamentos e
    /// cargos são dados de negócio, e semeá-los seria inventar organização.
    /// </summary>
    public static async Task MigrateHrModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<HrDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}




