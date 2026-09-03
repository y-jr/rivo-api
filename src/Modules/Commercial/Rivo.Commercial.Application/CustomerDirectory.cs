using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Contracts;
using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Application;

/// <summary>
/// O contrato publicado de `commercial`. É por aqui que `finance` lê o cliente
/// para emitir, sem conhecer nada além de `Rivo.Commercial.Contracts`.
/// </summary>
public sealed class CustomerDirectory(ICustomerStore store) : ICustomerDirectory
{
    public async Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var cliente = await store.FindAsync(customerId, cancellationToken);

        return cliente is null ? null : ToReference(cliente);
    }

    public async Task<CustomerReference?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cliente = await store.FindByUserIdAsync(userId, cancellationToken);

        return cliente is null ? null : ToReference(cliente);
    }

    internal static CustomerReference ToReference(Customer customer) =>
        new(
            customer.Id,
            customer.Name,
            customer.TaxId,
            ToContract(customer.Status),
            new Contracts.BillingAddress(
                customer.BillingAddress.Detail,
                customer.BillingAddress.City,
                customer.BillingAddress.Country));

    /// <summary>
    /// Traduz o estado do domínio para o publicado. Os dois enumerados existem
    /// em duplicado de propósito (ADR-010) — o `switch` exaustivo faz o
    /// compilador avisar quando um dos lados crescer sem o outro.
    /// </summary>
    internal static Contracts.CustomerStatus ToContract(Domain.CustomerStatus status) => status switch
    {
        Domain.CustomerStatus.Active => Contracts.CustomerStatus.Active,
        Domain.CustomerStatus.Inactive => Contracts.CustomerStatus.Inactive,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Estado sem correspondência publicada."),
    };
}
