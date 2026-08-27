using Microsoft.EntityFrameworkCore;
using Rivo.Documents.Application;
using Rivo.Documents.Domain;

namespace Rivo.Documents.Infrastructure.Persistence;

public sealed class DocumentRepository(DocumentsDbContext context) : IDocumentRepository
{
    public async Task<Document?> FindAsync(Guid documentId, CancellationToken cancellationToken) =>
        await context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public async Task<IReadOnlyList<Document>> FindManyAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken) =>
        await context.Documents
            .AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Document>> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = context.Documents
            .AsNoTracking()
            .Where(d => d.VoidedAt == null);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizada = category.Trim();
            query = query.Where(d => d.Category == normalizada);
        }

        // A janela é sobre a data de carregamento, e é inclusiva nos dois
        // extremos: quem pede "de 1 a 31" espera o dia 31 lá dentro.
        if (from is { } inicio)
        {
            var desde = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(d => d.UploadedAt >= desde);
        }

        if (to is { } fim)
        {
            var ate = new DateTimeOffset(fim.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            query = query.Where(d => d.UploadedAt <= ate);
        }

        return await query
            .OrderByDescending(d => d.UploadedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken) =>
        await context.Documents.AddAsync(document, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
