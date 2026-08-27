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

    /// <summary>
    /// Documentos por categoria e janela de carregamento, mais recentes
    /// primeiro.
    ///
    /// <para>
    /// <strong>Só os disponíveis.</strong> Um documento anulado deixou de
    /// servir, e listá-lo faria alguém tentar descarregá-lo para receber um
    /// 404 — o registo continua na base de dados por BR-14, e é isso que
    /// interessa a quem audita, não a quem procura um ficheiro.
    /// </para>
    /// </summary>
    /// <param name="limit">
    /// Tecto de resultados. <strong>A listagem é sempre limitada</strong>:
    /// sem tecto, esta rota cresce com o arquivo inteiro e o primeiro ano de
    /// uso torna-a inutilizável.
    /// </param>
    Task<IReadOnlyList<Document>> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int limit,
        CancellationToken cancellationToken);

    Task AddAsync(Document document, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
