using Microsoft.Extensions.Options;
using Rivo.Documents.Application;

namespace Rivo.Documents.Infrastructure.Storage;

/// <summary>
/// Armazenamento em sistema de ficheiros, sobre um volume.
///
/// <para>
/// Escolhido em vez de MinIO ou S3 para não acrescentar um serviço à stack
/// nesta fase. Trocar por armazenamento de objectos é implementar
/// <see cref="IDocumentStorage"/> — nada acima desta classe muda.
/// </para>
///
/// <para>
/// ⚠ <strong>Sem cifra em repouso.</strong> `standards/security.md` exige
/// AES-256 para anexos. Cifrar aqui exigiria gestão de chaves, que é decisão
/// pendente, e criptografia com chave mal gerida é pior do que a ausência
/// assinalada. Ver K11 em state/known-issues.md.
/// </para>
/// </summary>
public sealed class FileSystemDocumentStorage(IOptions<DocumentStorageOptions> options) : IDocumentStorage
{
    private readonly string _root = options.Value.RootPath;

    public async Task<string> SaveAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        // Reparte por dois níveis a partir do identificador. Um único
        // directório com centenas de milhares de ficheiros degrada operações
        // de sistema de ficheiros em muitas plataformas.
        var id = documentId.ToString("N");
        var relativeDirectory = Path.Combine(id[..2], id[2..4]);
        var absoluteDirectory = Path.Combine(_root, relativeDirectory);

        Directory.CreateDirectory(absoluteDirectory);

        // O nome em disco é o identificador, não o nome original: evita
        // colisões, travessia de caminhos e caracteres inválidos.
        var relativePath = Path.Combine(relativeDirectory, id);

        await using var file = File.Create(Path.Combine(_root, relativePath));
        await content.CopyToAsync(file, cancellationToken);

        // Guarda-se sempre com barras normais, para que o caminho gravado não
        // dependa do sistema onde foi criado.
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(_root, storagePath.Replace('/', Path.DirectorySeparatorChar));

        // Confirma que o caminho resolvido continua dentro da raiz. Protege
        // contra um valor manipulado na base de dados.
        var canonical = Path.GetFullPath(absolute);
        var canonicalRoot = Path.GetFullPath(_root);

        if (!canonical.StartsWith(canonicalRoot, StringComparison.Ordinal) || !File.Exists(canonical))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(canonical));
    }
}

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>Raiz do armazenamento. Em Docker, aponta para um volume.</summary>
    public string RootPath { get; init; } = "/var/rivo/documents";
}
