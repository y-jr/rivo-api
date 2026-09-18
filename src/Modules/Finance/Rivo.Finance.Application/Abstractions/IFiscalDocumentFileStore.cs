using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Persistência dos ficheiros de documentos fiscais já compostos.
/// </summary>
public interface IFiscalDocumentFileStore
{
    /// <summary>
    /// O papel já gerado para este documento <strong>no estado em que ele
    /// está</strong>.
    ///
    /// <para>
    /// <paramref name="reflectsCancellation"/> faz parte da chave de procura e
    /// não é um filtro acessório: pedir o ficheiro de uma factura anulada tem de
    /// devolver o que mostra a anulação, e não o que foi gerado antes. Sem este
    /// parâmetro, o primeiro ficheiro encontrado ganhava — e podia ser o errado.
    /// </para>
    /// </summary>
    Task<FiscalDocumentFile?> FindAsync(
        FiscalDocumentKind kind,
        Guid sourceDocumentId,
        bool reflectsCancellation,
        CancellationToken cancellationToken);

    Task AddAsync(FiscalDocumentFile file, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
