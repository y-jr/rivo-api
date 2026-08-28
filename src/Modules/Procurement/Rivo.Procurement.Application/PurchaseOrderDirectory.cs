using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Contracts;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application;

/// <summary>
/// O contrato publicado da Ordem de Compra. É por aqui que `finance` lê o
/// encomendado e o recebido, para os pôr ao lado do facturado — o 3-way match
/// fecha-se do lado de `finance`, porque é lá que a factura vive.
/// </summary>
public sealed class PurchaseOrderDirectory(IProcurementStore store) : IPurchaseOrderDirectory
{
    public async Task<PurchaseOrderReference?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var ordem = await store.FindOrderAsync(purchaseOrderId, cancellationToken);

        if (ordem is null)
        {
            return null;
        }

        var recebido = await store.ReceivedByOrderLineAsync(ordem.Id, cancellationToken);

        var linhas = ordem.Lines
            .Select(l => new PurchaseOrderLineReference(
                l.Id,
                l.Description,
                l.Quantity,
                recebido.TryGetValue(l.Id, out var quanto) ? quanto : 0m,
                l.UnitPrice,
                l.LineTotal))
            .ToList();

        return new PurchaseOrderReference(
            ordem.Id,
            ordem.SupplierId,
            ordem.Currency,
            ordem.Total,
            ToContract(ordem.Status),
            linhas);
    }

    /// <summary>
    /// Traduz o estado do domínio para o publicado. Os dois enumerados existem
    /// em duplicado de propósito (ADR-010) — o <c>switch</c> exaustivo faz o
    /// compilador avisar quando um dos lados crescer sem o outro.
    /// </summary>
    internal static PurchaseOrderReferenceStatus ToContract(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Issued => PurchaseOrderReferenceStatus.Issued,
        PurchaseOrderStatus.Cancelled => PurchaseOrderReferenceStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Estado sem correspondência publicada."),
    };
}
