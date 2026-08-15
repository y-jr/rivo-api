using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Documents.Application;
using Rivo.Documents.Contracts;
using Rivo.Documents.Infrastructure.Persistence;
using Rivo.Documents.Infrastructure.Storage;

namespace Rivo.Documents.Infrastructure;

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<DocumentsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DocumentsDbContext.Schema)
                    // Resiliencia de ligacao: a base de dados pode nao estar
                    // pronta no arranque (o depends_on do compose so vale no up,
                    // nao no restart), e em producao ha failover e reinicios.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention());

        services
            .AddOptions<DocumentStorageOptions>()
            .Bind(configuration.GetSection(DocumentStorageOptions.SectionName));

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        services.AddScoped<IDocumentCatalogue, DocumentCatalogue>();
        services.AddScoped<UploadDocument>();
        services.AddScoped<DownloadDocument>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in DocumentPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    /// <summary>
    /// Aplica as migrações e garante que a raiz de armazenamento existe.
    ///
    /// Sem seed: documentos são dados de negócio.
    /// </summary>
    public static async Task InitialiseDocumentsModuleAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<DocumentsDbContext>()
            .Database.MigrateAsync(cancellationToken);

        // Falhar aqui, no arranque, é melhor do que falhar no primeiro upload:
        // um volume mal montado deixa de ser surpresa em produção.
        var storage = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DocumentStorageOptions>>().Value;

        Directory.CreateDirectory(storage.RootPath);
    }
}



