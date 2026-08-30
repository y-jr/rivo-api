using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Application.UseCases;
using Rivo.Projects.Contracts;
using Rivo.Projects.Infrastructure.Persistence;

namespace Rivo.Projects.Infrastructure;

/// <summary>
/// Composição do módulo `projects` — ver `modules/projects.md`. Marco,
/// Tarefa e Orçamento têm regra de negócio própria desde 2026-08-30;
/// Alocação de Recursos continua por fazer.
/// </summary>
public static class ProjectsModuleExtensions
{
    public static IServiceCollection AddProjectsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<ProjectsDbContext>(options => options
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", ProjectsDbContext.Schema)
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IProjectStore, ProjectStore>();

        services.AddScoped<ListProjects>();
        services.AddScoped<GetProject>();
        services.AddScoped<OpenProject>();
        services.AddScoped<CloseProject>();
        services.AddScoped<AddMilestone>();
        services.AddScoped<ReachMilestone>();
        services.AddScoped<AddTask>();
        services.AddScoped<AssignTask>();
        services.AddScoped<CompleteTask>();
        services.AddScoped<CancelTask>();
        services.AddScoped<SetProjectBudget>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectsPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static async Task MigrateProjectsModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ProjectsDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}
