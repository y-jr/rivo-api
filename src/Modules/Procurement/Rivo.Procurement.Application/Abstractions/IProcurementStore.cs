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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
