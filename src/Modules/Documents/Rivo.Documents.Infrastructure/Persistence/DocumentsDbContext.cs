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
}
