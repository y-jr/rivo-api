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

    /// <summary>
    /// Quanto falta receber de uma factura: o total, menos o que já foi
    /// creditado, menos o que já foi recebido — contando só os documentos
    /// **não anulados**.
    ///
    /// <para>
    /// <strong>É a invariante que nenhum agregado impõe sozinho.</strong> Nem a
    /// factura vê as suas notas de crédito, nem o recibo vê os outros recibos.
    /// Vive aqui pela mesma razão que a unicidade do NIF vive no store de
    /// `commercial`: é uma regra sobre o conjunto.
    /// </para>
    ///
    /// <para>
    /// Calculado, não guardado. Um saldo em coluna seria um ponto de contenção a
    /// cada recebimento, e ficaria errado em silêncio no dia em que alguém
    /// estornasse um recibo sem o recalcular.
    /// </para>
    /// </summary>
    Task<decimal> OutstandingAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// A mesma conta de <see cref="OutstandingAsync"/>, somada sobre todas as
    /// facturas não anuladas nesta moeda — três agregações de base de dados
    /// (facturado, creditado, recebido), não uma consulta por factura.
    /// Primeiro consumidor: <c>IReceivablesOverview</c> (Fase 8, ADR-041).
    /// </summary>
    Task<decimal> SumOutstandingAsync(string currency, CancellationToken cancellationToken);

    /// <summary>
    /// A mesma conta de <see cref="SumOutstandingAsync"/>, restrita a um
    /// cliente. Primeiro consumidor: o Portal do Cliente (ADR-043) — "o que
    /// devo eu, cliente X" nunca pode somar a dívida de outro.
    /// </summary>
    Task<decimal> SumOutstandingForCustomerAsync(
        Guid customerId, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// Soma do valor líquido das facturas de venda emitidas no período,
    /// nesta moeda, não anuladas.
    /// </summary>
    Task<decimal> SumNetInvoicedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// Soma do valor líquido das notas de crédito emitidas no período,
    /// nesta moeda, não anuladas.
    /// </summary>
    Task<decimal> SumNetCreditedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>A mesma conta de <see cref="SumNetInvoicedAsync"/>, restrita a um cliente.</summary>
    Task<decimal> SumNetInvoicedForCustomerAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>A mesma conta de <see cref="SumNetCreditedAsync"/>, restrita a um cliente.</summary>
    Task<decimal> SumNetCreditedForCustomerAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// Os clientes (com <c>CustomerId</c> real — consumidor final fica de
    /// fora) que mais facturaram no período, nesta moeda, por valor
    /// líquido não anulado. Devolve só o identificador e o total: o nome
    /// resolve-se pelo contrato de `commercial`, não se copia aqui.
    /// </summary>
    Task<IReadOnlyList<CustomerInvoicedTotal>> TopCustomersByInvoicedAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken);

    Task<CreditNote?> FindCreditNoteAsync(Guid creditNoteId, CancellationToken cancellationToken);

    Task<CreditNote?> FindCreditNoteForUpdateAsync(Guid creditNoteId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditNote>> ListCreditNotesAsync(
        Guid? salesInvoiceId,
        CancellationToken cancellationToken);

    Task AddCreditNoteAsync(CreditNote note, CancellationToken cancellationToken);

    Task<Receipt?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<Receipt?> FindReceiptForUpdateAsync(Guid receiptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Receipt>> ListReceiptsAsync(
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    Task AddReceiptAsync(Receipt receipt, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Um cliente e o que facturou num período — sem o nome, que vem de `commercial`.</summary>
public sealed record CustomerInvoicedTotal(Guid CustomerId, decimal NetTotal);
