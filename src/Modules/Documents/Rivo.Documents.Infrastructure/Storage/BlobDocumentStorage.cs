using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Rivo.Documents.Application;

namespace Rivo.Documents.Infrastructure.Storage;

/// <summary>
/// Armazenamento em Azure Blob Storage.
///
/// <para>
/// <strong>Fecha o K11.</strong> `standards/security.md` exige cifra em
/// repouso para anexos, e o armazenamento em sistema de ficheiros guardava-os
/// em claro. O defeito ficou aberto tanto tempo por uma razão concreta: cifrar
/// na aplicação exigia gestão de chaves, e criptografia com chave mal gerida é
/// pior do que a ausência assinalada. Aqui a cifra é do serviço — a aplicação
/// não vê nem gere chave nenhuma.
/// </para>
///
/// <para>
/// <strong>Sem credenciais.</strong> A autenticação é pela identidade gerida
/// do App Service, a que o Bicep atribui `Storage Blob Data Contributor`
/// (ADR-027). Não há chave de conta em configuração, nem connection string com
/// segredo — que é o modo habitual de integrar Blob Storage e o que se evita
/// aqui de propósito.
/// </para>
/// </summary>
public sealed class BlobDocumentStorage : IDocumentStorage
{
    private readonly BlobContainerClient _container;

    public BlobDocumentStorage(IOptions<DocumentStorageOptions> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.AccountName))
        {
            throw new InvalidOperationException(
                $"'{DocumentStorageOptions.SectionName}:AccountName' é obrigatório para usar Blob Storage.");
        }

        var endpoint = new Uri($"https://{settings.AccountName}.blob.core.windows.net");

        // `DefaultAzureCredential` resolve a identidade gerida em Azure e as
        // credenciais de programador na máquina local, sem que o código saiba
        // qual está a usar.
        var service = new BlobServiceClient(endpoint, new DefaultAzureCredential());

        _container = service.GetBlobContainerClient(settings.Container);
    }

    public async Task<string> SaveAsync(Guid documentId, Stream content, CancellationToken cancellationToken)
    {
        var path = BuildPath(documentId);

        // `overwrite: false` porque o identificador é UUIDv7 e nunca se repete
        // (ADR-019). Se algum dia se repetir, é melhor falhar ruidosamente do
        // que substituir em silêncio um anexo já ligado a um registo.
        await _container
            .GetBlobClient(path)
            .UploadAsync(content, overwrite: false, cancellationToken);

        return path;
    }

    public async Task<Stream?> OpenAsync(string storagePath, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container
                .GetBlobClient(storagePath)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);

            return response.Value.Content;
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            // Mesmo contrato da implementação em sistema de ficheiros: o
            // ausente devolve nulo, não excepção. Quem chama distingue "não
            // existe" de "falhou".
            return null;
        }
    }

    /// <summary>
    /// Mantém a repartição por dois níveis da implementação em sistema de
    /// ficheiros.
    ///
    /// <para>
    /// No Blob Storage não é necessária — o espaço de nomes é plano e não
    /// degrada com o volume. Mantém-se porque torna os caminhos comparáveis
    /// entre as duas implementações, e porque prefixos tornam a listagem por
    /// intervalo utilizável em diagnóstico.
    /// </para>
    /// </summary>
    private static string BuildPath(Guid documentId)
    {
        var id = documentId.ToString("N");
        return $"{id[..2]}/{id[2..4]}/{id}";
    }
}
