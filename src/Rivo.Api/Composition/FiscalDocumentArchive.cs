using Rivo.Audit.Contracts;
using Rivo.Documents.Application;
using Rivo.Finance.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga <see cref="IFiscalDocumentArchive"/> — declarado por `finance` — ao
/// repositório de ficheiros de `documents`.
///
/// <para>
/// Vive no composition root, ao lado das outras ligações do género
/// (<c>FinancePaymentApproval</c>, <c>InventoryApprovalSubmission</c>): é o único
/// sítio que conhece os dois módulos. Ver o próprio contrato para a razão de não
/// estar em <c>Rivo.Documents.Contracts</c>.
/// </para>
/// </summary>
public sealed class FiscalDocumentArchive(
    UploadDocument upload,
    DownloadDocument download) : IFiscalDocumentArchive
{
    /// <summary>
    /// A categoria com que os documentos fiscais ficam arquivados.
    ///
    /// <para>
    /// Própria, e não «geral»: <c>GET /documents?category=</c> passa a poder
    /// listar exactamente os papéis emitidos, que é a pergunta que uma
    /// inspecção faz.
    /// </para>
    /// </summary>
    public const string Categoria = "documento-fiscal";

    public async Task<ArchivedDocument> StoreAsync(
        string fileName,
        byte[] content,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);

        var descriptor = await upload.ExecuteAsync(
            fileName,
            "application/pdf",
            Categoria,
            stream,
            context,
            cancellationToken);

        return new ArchivedDocument(descriptor.DocumentId, descriptor.ContentHash);
    }

    public async Task<byte[]?> FetchAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var conteudo = await download.ExecuteAsync(documentId, cancellationToken);

        if (conteudo is null)
        {
            return null;
        }

        await using var origem = conteudo.Content;
        using var buffer = new MemoryStream();

        await origem.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
