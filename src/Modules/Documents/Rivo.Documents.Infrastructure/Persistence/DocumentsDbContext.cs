using Microsoft.EntityFrameworkCore;
using Rivo.Documents.Domain;

namespace Rivo.Documents.Infrastructure.Persistence;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public const string Schema = "documents";

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Document>(document =>
        {
            document.ToTable("document");
            document.HasKey(d => d.Id);
            // Concorrência optimista (ADR-002, ADR-025).
            document.Property(d => d.Version).IsConcurrencyToken();

            document.Property(d => d.FileName).HasMaxLength(400).IsRequired();
            document.Property(d => d.ContentType).HasMaxLength(200).IsRequired();
            document.Property(d => d.Category).HasMaxLength(100).IsRequired();

            // SHA-256 em hexadecimal minúsculo: 64 caracteres, sempre.
            document.Property(d => d.ContentHash).HasMaxLength(64).IsRequired().IsFixedLength();

            document.Property(d => d.StoragePath).HasMaxLength(500).IsRequired();

            // Consulta de duplicados: mesmo conteúdo carregado duas vezes.
            document.HasIndex(d => d.ContentHash);

            document.HasIndex(d => d.Category);

            // Sem chave estrangeira para registos de negócio: a ligação vive
            // no contexto de origem (ADR-009). `documents` não conhece `hr`,
            // `finance` nem nenhum outro módulo.
        });
    }

    /// <summary>
    /// Incrementa o contador de concorrência de tudo o que vai ser alterado.
    ///
    /// <para>
    /// O domínio nunca mexe no <c>Version</c>: obrigá-lo a lembrar-se disso em
    /// cada método que altera estado seria uma regra que se esquece uma vez e
    /// falha em silêncio para sempre. Aqui é impossível esquecer.
    /// </para>
    ///
    /// <para>
    /// Subir o <c>CurrentValue</c> basta — o EF Core usa o <c>OriginalValue</c>
    /// na cláusula <c>WHERE</c>, que é o que detecta a escrita concorrente.
    /// </para>
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Version");

            if (version?.CurrentValue is int current)
            {
                version.CurrentValue = current + 1;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
