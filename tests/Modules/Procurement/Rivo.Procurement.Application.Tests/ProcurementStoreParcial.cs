using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.Tests;

/// <summary>
/// Base para dobras de <see cref="IProcurementStore"/> que só precisam de
/// alguns membros.
///
/// <para>
/// Tudo lança por omissão, e é essa a parte útil: se um caso de uso passar a
/// tocar num membro que o teste não previu, o teste falha <em>a dizer qual</em>,
/// em vez de receber <c>null</c> e seguir por um caminho que ninguém quis
/// exercitar. Mesmo desenho de <c>HrStoreParcial</c>.
/// </para>
/// </summary>
internal abstract class ProcurementStoreParcial : IProcurementStore
{
    private static Task<T> NaoUsado<T>([System.Runtime.CompilerServices.CallerMemberName] string membro = "") =>
        throw new NotSupportedException($"O teste não previu uma chamada a {membro}.");

    private static Task NaoUsado([System.Runtime.CompilerServices.CallerMemberName] string membro = "") =>
        throw new NotSupportedException($"O teste não previu uma chamada a {membro}.");

    public virtual Task<Supplier?> FindSupplierAsync(Guid supplierId, CancellationToken cancellationToken) => NaoUsado<Supplier?>();
    public virtual Task<Supplier?> FindSupplierForUpdateAsync(Guid supplierId, CancellationToken cancellationToken) => NaoUsado<Supplier?>();
    public virtual Task<Supplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken cancellationToken) => NaoUsado<Supplier?>();
    public virtual Task<IReadOnlyList<Supplier>> ListSuppliersAsync(bool includeInactive, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<Supplier>>();
    public virtual Task AddSupplierAsync(Supplier supplier, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<PurchaseRequisition?> FindRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) => NaoUsado<PurchaseRequisition?>();
    public virtual Task<PurchaseRequisition?> FindRequisitionForUpdateAsync(Guid requisitionId, CancellationToken cancellationToken) => NaoUsado<PurchaseRequisition?>();
    public virtual Task<IReadOnlyList<PurchaseRequisition>> ListRequisitionsAsync(Guid? requestedByEmployeeId, RequisitionStatus? status, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<PurchaseRequisition>>();
    public virtual Task AddRequisitionAsync(PurchaseRequisition requisition, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<PurchaseOrder?> FindOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) => NaoUsado<PurchaseOrder?>();
    public virtual Task<PurchaseOrder?> FindOrderForUpdateAsync(Guid purchaseOrderId, CancellationToken cancellationToken) => NaoUsado<PurchaseOrder?>();
    public virtual Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(Guid? requisitionId, Guid? supplierId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<PurchaseOrder>>();
    public virtual Task<decimal> OrderedAgainstRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) => NaoUsado<decimal>();
    public virtual Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<GoodsReceipt?> FindReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken) => NaoUsado<GoodsReceipt?>();
    public virtual Task<GoodsReceipt?> FindReceiptForUpdateAsync(Guid goodsReceiptId, CancellationToken cancellationToken) => NaoUsado<GoodsReceipt?>();
    public virtual Task<IReadOnlyList<GoodsReceipt>> ListReceiptsAsync(Guid? purchaseOrderId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<GoodsReceipt>>();
    public virtual Task<IReadOnlyDictionary<Guid, decimal>> ReceivedByOrderLineAsync(Guid purchaseOrderId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyDictionary<Guid, decimal>>();
    public virtual Task<bool> HasReceiptsAsync(Guid purchaseOrderId, CancellationToken cancellationToken) => NaoUsado<bool>();
    public virtual Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task SaveChangesAsync(CancellationToken cancellationToken) => NaoUsado();
}
