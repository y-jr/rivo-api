namespace Rivo.Documents.Contracts;

/// <summary>
/// Superfície publicada de `documents`. Assembly sem dependências (ADR-017).
///
/// <para>
/// Serve para os módulos consumidores lerem <strong>metadados</strong> dos
/// documentos que anexaram. O conteúdo do ficheiro obtém-se pela API do
/// próprio módulo, com a permissão respectiva.
/// </para>
///
/// <para>
/// `documents` <strong>não</strong> conhece os registos de negócio a que os
/// documentos estão ligados: a ligação vive no contexto de origem, numa
/// tabela própria com chaves estrangeiras reais (ADR-009).
/// </para>
/// </summary>
public interface IDocumentCatalogue
{
    Task<DocumentDescriptor?> FindAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta em lote, para que um consumidor liste os anexos de um registo
    /// sem fazer N chamadas.
    /// </summary>
    Task<IReadOnlyList<DocumentDescriptor>> FindManyAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken);
}

/// <param name="Category">
/// Classificação atribuída pelo contexto de origem. `documents` guarda-a mas
/// não a interpreta — é o módulo de origem que sabe o que significa e que
/// prazo de retenção lhe corresponde (BR-15).
/// </param>
/// <param name="ContentHash">SHA-256 do conteúdo, para verificação de integridade.</param>
public sealed record DocumentDescriptor(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string Category,
    string ContentHash,
    Guid? UploadedBy,
    DateTimeOffset UploadedAt);

/// <summary>Catálogo de permissões de `documents`, declarado pelo próprio módulo.</summary>
public static class DocumentPermissions
{
    public const string Read = "documents.read";
    public const string Write = "documents.write";

    public static readonly IReadOnlyList<string> All = [Read, Write];
}
