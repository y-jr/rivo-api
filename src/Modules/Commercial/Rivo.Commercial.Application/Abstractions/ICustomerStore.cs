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

    /// <summary>
    /// Procura pela conta de `identity` ligada. Existe para a verificação de
    /// unicidade em <c>LinkCustomerAccount</c> (ADR-043) — mesma razão de
    /// <c>IHrStore.FindEmployeeByUserIdAsync</c>.
    /// </summary>
    Task<Customer?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    // --- Histórico do vínculo conta↔cliente (ADR-055) ---

    Task AddAccountLinkAsync(CustomerAccountLink link, CancellationToken cancellationToken);

    /// <summary>
    /// O episódio ainda aberto deste cliente, se existir.
    ///
    /// <para>
    /// ⚠ <strong>Não é por aqui que o Portal do Cliente resolve «o próprio».</strong>
    /// Isso continua a ser <see cref="FindByUserIdAsync"/>, que lê
    /// <c>Customer.UserId</c> — o ADR-055 acrescentou história e não mexeu no
    /// caminho de resolução de identidade, de propósito.
    /// </para>
    /// </summary>
    Task<CustomerAccountLink?> FindOpenAccountLinkAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Todos os episódios de um cliente, do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<CustomerAccountLink>> ListAccountLinksAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
