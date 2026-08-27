using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Contracts;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application;

/// <summary>
/// O contrato publicado de `procurement`. É por aqui que `finance` lê o
/// fornecedor, sem conhecer nada além de `Rivo.Procurement.Contracts`.
/// </summary>
public sealed class SupplierDirectory(IProcurementStore store) : ISupplierDirectory
{
    public async Task<SupplierReference?> FindAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var fornecedor = await store.FindSupplierAsync(supplierId, cancellationToken);

        return fornecedor is null ? null : ToReference(fornecedor);
    }

    public async Task<SupplierReference?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return null;
        }

        // Normalizado do mesmo modo que o agregado o guarda. Sem isto, procurar
        // pelo NIF como vem na factura — com espaços — não encontrava nada.
        var normalizado = taxId.Replace(" ", string.Empty).Trim().ToUpperInvariant();

        var fornecedor = await store.FindSupplierByTaxIdAsync(normalizado, cancellationToken);

        return fornecedor is null ? null : ToReference(fornecedor);
    }

    internal static SupplierReference ToReference(Supplier supplier) =>
        new(supplier.Id, supplier.Name, supplier.TaxId, supplier.Iban, ToContract(supplier.Status));

    /// <summary>
    /// Traduz o estado do domínio para o publicado. Os dois enumerados existem
    /// em duplicado de propósito (ADR-010) — o <c>switch</c> exaustivo faz o
    /// compilador avisar quando um dos lados crescer sem o outro.
    /// </summary>
    internal static Contracts.SupplierStatus ToContract(Domain.SupplierStatus status) => status switch
    {
        Domain.SupplierStatus.Active => Contracts.SupplierStatus.Active,
        Domain.SupplierStatus.Inactive => Contracts.SupplierStatus.Inactive,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Estado sem correspondência publicada."),
    };
}
