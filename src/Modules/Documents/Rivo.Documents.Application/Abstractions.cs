using Rivo.Documents.Domain;

namespace Rivo.Documents.Application;

/// <summary>
/// Armazenamento do conteúdo. Definido aqui porque a camada Application
/// precisa de guardar bytes mas não pode conhecer o sistema de ficheiros nem
/// o serviço de objectos.
///
/// Hoje implementado sobre o sistema de ficheiros num volume; trocar por S3
/// é implementar esta interface. A decisão do serviço de produção continua
/// pendente.
/// </summary>
public interface IDocumentStorage
{
    /// <returns>Localização opaca, guardada em <see cref="Document.StoragePath"/>.</returns>
    Task<string> SaveAsync(Guid documentId, Stream content, CancellationToken cancellationToken);

    Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken);
}

public interface IDocumentRepository
{
    Task<Document?> FindAsync(Guid documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> FindManyAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken);

    Task AddAsync(Document document, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
