using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Application.Abstractions;

/// <summary>
/// Persistência de `commercial`. Definida aqui e implementada em
/// Infrastructure, para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface ICustomerStore
{
    /// <summary>Sem rastreio: quem lê não altera.</summary>
    Task<Customer?> FindAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Rastreado: quem procura assim vai alterar.</summary>
    Task<Customer?> FindForUpdateAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Procura pelo NIF normalizado.
    ///
    /// <para>
    /// Existe para a verificação de unicidade, que o agregado não pode fazer
    /// por não ver o conjunto. É a primeira linha; a segunda é o índice único
    /// em `commercial.customer`.
    /// </para>
    /// </summary>
    Task<Customer?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
