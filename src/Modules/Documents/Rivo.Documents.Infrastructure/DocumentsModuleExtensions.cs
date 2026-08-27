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
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", DocumentsDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container e
                    // vive noutra maquina. Falhas de rede transitorias sao normais,
                    // nao excepcionais — e o arranque pode apanhar a base indisponivel.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .UseSnakeCaseNamingConvention());

        services
            .AddOptions<DocumentStorageOptions>()
            .Bind(configuration.GetSection(DocumentStorageOptions.SectionName));

        services.AddScoped<IDocumentRepository, DocumentRepository>();

        // Blob Storage quando há conta configurada; sistema de ficheiros quando
        // não há. A escolha é por configuração e não por ambiente, para que o
        // desenvolvimento local continue a correr sem Azure nenhum e o mesmo
        // binário sirva os dois casos (ADR-027).
        //
        // O K11 — anexos sem cifra em repouso — fica fechado no caminho de
        // Blob Storage. **Continua aberto no de sistema de ficheiros**, que é
        // o usado localmente, e é aceitável aí: não guarda dados reais.
        var storage = configuration
            .GetSection(DocumentStorageOptions.SectionName)
            .Get<DocumentStorageOptions>() ?? new DocumentStorageOptions();

        if (string.IsNullOrWhiteSpace(storage.AccountName))
        {
            services.AddSingleton<IDocumentStorage, FileSystemDocumentStorage>();
        }
        else
        {
            services.AddSingleton<IDocumentStorage, BlobDocumentStorage>();
        }
        services.AddScoped<IDocumentCatalogue, DocumentCatalogue>();
        services.AddScoped<UploadDocument>();
        services.AddScoped<DownloadDocument>();
        services.AddScoped<ListDocuments>();

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
    public static async Task MigrateDocumentsModuleAsync(
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



