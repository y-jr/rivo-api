using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Persistência de `finance`/AR. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface ISalesInvoiceStore
{
    /// <summary>
    /// A série, <strong>rastreada</strong>. Quem a procura vai atribuir um
    /// número, e é o contador de concorrência desta linha que faz duas emissões
    /// simultâneas colidirem em vez de saírem com o mesmo número.
    /// </summary>
    Task<DocumentSeries?> FindSeriesForAllocationAsync(
        DocumentType type,
        string code,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentSeries>> ListSeriesAsync(CancellationToken cancellationToken);

    Task AddSeriesAsync(DocumentSeries series, CancellationToken cancellationToken);

    Task<bool> SeriesExistsAsync(DocumentType type, string code, CancellationToken cancellationToken);

    /// <summary>Sem rastreio, com as linhas: é leitura.</summary>
    Task<SalesInvoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Rastreada: quem a procura assim vai anulá-la.</summary>
    Task<SalesInvoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesInvoice>> ListAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
