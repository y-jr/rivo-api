using System.Security.Cryptography;
using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Documents.Domain;

namespace Rivo.Documents.Application;

/// <summary>
/// Armazena um ficheiro e regista os seus metadados.
///
/// A ordem importa: o conteúdo é escrito primeiro e só depois se grava o
/// registo. Se a escrita falhar, não fica metadado a apontar para um ficheiro
/// inexistente. O inverso — ficheiro órfão sem registo — é recuperável por
/// limpeza, e é o modo de falha menos mau.
/// </summary>
public sealed class UploadDocument(
    IDocumentRepository repository,
    IDocumentStorage storage,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<DocumentDescriptor> ExecuteAsync(
        string fileName,
        string contentType,
        string category,
        Stream content,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var documentId = Guid.CreateVersion7();

        // Hash calculado sobre o que vai ser guardado, num só percurso do
        // stream. Serve para verificar integridade e detectar duplicados.
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;

        var storagePath = await storage.SaveAsync(documentId, buffer, cancellationToken);

        var document = Document.Store(
            fileName, contentType, buffer.Length, category, hash, storagePath,
            context.ActorId, clock.GetUtcNow());

        await repository.AddAsync(document, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                DocumentAuditActions.Uploaded,
                DocumentAuditEntityTypes.Document,
                document.Id.ToString(),
                context,
                // Nome e categoria bastam para a trilha. O conteúdo nunca entra
                // na auditoria — pode ser sensível (BR-16).
                NewValue: $$"""{"fileName":"{{document.FileName}}","category":"{{document.Category}}"}"""),
            cancellationToken);

        return Map(document);
    }

    internal static DocumentDescriptor Map(Document document) =>
        new(
            document.Id,
            document.FileName,
            document.ContentType,
            document.SizeInBytes,
            document.Category,
            document.ContentHash,
            document.UploadedBy,
            document.UploadedAt);
}

/// <summary>Obtém o conteúdo de um documento para descarregar.</summary>
public sealed class DownloadDocument(IDocumentRepository repository, IDocumentStorage storage)
{
    public async Task<DocumentContent?> ExecuteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await repository.FindAsync(documentId, cancellationToken);

        if (document is null || !document.IsAvailable)
        {
            return null;
        }

        var content = await storage.OpenAsync(document.StoragePath, cancellationToken);

        // Registo sem ficheiro é inconsistência de armazenamento, não "não
        // encontrado". Devolve-se nulo e deixa-se a API decidir o código.
        return content is null ? null : new DocumentContent(content, document.ContentType, document.FileName);
    }
}

public sealed record DocumentContent(Stream Content, string ContentType, string FileName);

/// <summary>Implementa o contrato publicado: metadados para consumidores.</summary>
public sealed class DocumentCatalogue(IDocumentRepository repository) : IDocumentCatalogue
{
    public async Task<DocumentDescriptor?> FindAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await repository.FindAsync(documentId, cancellationToken);

        return document is null || !document.IsAvailable ? null : UploadDocument.Map(document);
    }

    public async Task<IReadOnlyList<DocumentDescriptor>> FindManyAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var documents = await repository.FindManyAsync(documentIds, cancellationToken);

        return [.. documents.Where(d => d.IsAvailable).Select(UploadDocument.Map)];
    }
}

public static class DocumentAuditActions
{
    public const string Uploaded = "documents.document.uploaded";
}

public static class DocumentAuditEntityTypes
{
    public const string Document = "documents.document";
}
