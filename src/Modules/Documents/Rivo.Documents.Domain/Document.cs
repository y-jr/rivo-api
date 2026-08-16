namespace Rivo.Documents.Domain;

/// <summary>
/// Um ficheiro armazenado, com os seus metadados.
///
/// <para>
/// <strong>`documents` não interpreta o conteúdo nem o significado de
/// negócio.</strong> Guarda o ficheiro, os metadados e o hash; a
/// classificação e o prazo de retenção pertencem ao contexto de origem, que é
/// o único que os conhece (BR-15).
/// </para>
///
/// <para>
/// <strong>Não referencia nenhum registo de negócio.</strong> A ligação vive
/// no contexto consumidor, numa tabela própria com chaves estrangeiras reais
/// (ADR-009). Isto substitui a chave polimórfica do desenho inicial e
/// devolve integridade referencial nos dois sentidos.
/// </para>
/// </summary>
public sealed class Document
{
    private Document()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        Category = string.Empty;
        ContentHash = string.Empty;
        StoragePath = string.Empty;
    }

    private Document(
        Guid id,
        string fileName,
        string contentType,
        long sizeInBytes,
        string category,
        string contentHash,
        string storagePath,
        Guid? uploadedBy,
        DateTimeOffset uploadedAt)
    {
        Id = id;
        FileName = fileName;
        ContentType = contentType;
        SizeInBytes = sizeInBytes;
        Category = category;
        ContentHash = contentHash;
        StoragePath = storagePath;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Contador de concorrência optimista (ADR-002, ADR-025).
    ///
    /// Incrementado pela infraestrutura ao gravar, nunca pelo domínio. O
    /// <c>private set</c> existe só para o EF Core o materializar.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Nome original, para devolver ao descarregar. Não é o nome em disco.</summary>
    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long SizeInBytes { get; private set; }

    public string Category { get; private set; }

    /// <summary>
    /// SHA-256 do conteúdo. Permite verificar que o ficheiro não foi alterado
    /// no armazenamento, e detectar duplicados sem ler o conteúdo todo.
    /// </summary>
    public string ContentHash { get; private set; }

    /// <summary>
    /// Localização no armazenamento. Opaco para quem consome — pode ser um
    /// caminho de ficheiro hoje e uma chave de objecto amanhã.
    /// </summary>
    public string StoragePath { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>
    /// Anulação lógica. Documentos sujeitos a retenção legal nunca são
    /// eliminados fisicamente (BR-14); o prazo é do contexto de origem.
    /// </summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    public static Document Store(
        string fileName,
        string contentType,
        long sizeInBytes,
        string category,
        string contentHash,
        string storagePath,
        Guid? uploadedBy,
        DateTimeOffset uploadedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        // Um ficheiro vazio quase sempre indica upload falhado. Aceitá-lo
        // deixaria um anexo inútil ligado a um registo de negócio.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);

        return new Document(
            Guid.CreateVersion7(),
            fileName.Trim(),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            sizeInBytes,
            category.Trim(),
            contentHash,
            storagePath,
            uploadedBy,
            uploadedAt);
    }

    public bool IsAvailable => VoidedAt is null;

    public void Void(DateTimeOffset now) => VoidedAt ??= now;
}
