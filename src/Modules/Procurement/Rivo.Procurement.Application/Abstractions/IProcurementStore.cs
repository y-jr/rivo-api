using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.Abstractions;

/// <summary>
/// Persistência de `procurement`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IProcurementStore
{
    /// <summary>Sem rastreio: quem lê não altera.</summary>
    Task<Supplier?> FindSupplierAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>Rastreado: quem procura assim vai alterar.</summary>
    Task<Supplier?> FindSupplierForUpdateAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>
    /// Procura pelo NIF normalizado.
    ///
    /// <para>
    /// Existe para a verificação de unicidade, que o agregado não pode fazer
    /// por não ver o conjunto. É a primeira linha; a segunda é o índice único
    /// em `procurement.supplier`.
    /// </para>
    /// </summary>
    Task<Supplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Supplier>> ListSuppliersAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken);

    Task<PurchaseRequisition?> FindRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task<PurchaseRequisition?> FindRequisitionForUpdateAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseRequisition>> ListRequisitionsAsync(
        Guid? requestedByEmployeeId,
        RequisitionStatus? status,
        CancellationToken cancellationToken);

    Task AddRequisitionAsync(PurchaseRequisition requisition, CancellationToken cancellationToken);

    Task<PurchaseOrder?> FindOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    Task<PurchaseOrder?> FindOrderForUpdateAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(
        Guid? requisitionId,
        Guid? supplierId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quanto já foi encomendado contra uma requisição, somando só as ordens em
    /// vigor.
    ///
    /// <para>
    /// <strong>É a invariante sobre o conjunto que o agregado não vê.</strong>
    /// Três ordens de metade cada passariam uma a uma; juntas encomendam uma vez
    /// e meia o que foi aprovado, e a alçada fica contornada sem que nada a
    /// tenha violado. Mesma forma do <c>CommittedAsync</c> de `finance`.
    /// </para>
    ///
    /// <para>As canceladas não contam: deixaram de ser compromisso.</para>
    /// </summary>
    Task<decimal> OrderedAgainstRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken);

    Task<GoodsReceipt?> FindReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken);

    Task<GoodsReceipt?> FindReceiptForUpdateAsync(Guid goodsReceiptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GoodsReceipt>> ListReceiptsAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quanto já foi recebido de cada linha de uma ordem, contando só as
    /// recepções em vigor.
    ///
    /// <para>
    /// <strong>Por linha, e não por total.</strong> Receber duas unidades de uma
    /// coisa e nenhuma de outra somaria certo no total e estaria errado em tudo
    /// o resto — e é exactamente a divergência que o 3-way match existe para
    /// apanhar.
    /// </para>
    ///
    /// <para>
    /// Linhas sem recepção nenhuma não aparecem no resultado. Quem lê trata a
    /// ausência como zero.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> ReceivedByOrderLineAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Se uma ordem já tem alguma recepção em vigor.
    ///
    /// <para>
    /// Existe para impedir cancelar uma ordem cuja mercadoria já chegou: o
    /// material está cá, e cancelar a encomenda não o faz desaparecer.
    /// </para>
    /// </summary>
    Task<bool> HasReceiptsAsync(Guid purchaseOrderId, CancellationToken cancellationToken);

    Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
