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

    public async Task AddAsync(Document document, CancellationToken cancellationToken) =>
        await context.Documents.AddAsync(document, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
