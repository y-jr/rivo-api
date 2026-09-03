namespace Rivo.Commercial.Contracts;

/// <summary>
/// Superfície publicada de `commercial`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>Âmbito reduzido por ADR-036.</strong> Daqui só sai o Cliente — o que
/// a emissão precisa. Lead, Oportunidade, Proposta, Contrato Comercial e Acção
/// de Cobrança continuam por fazer: são o funil comercial, e emitir não depende
/// dele.
/// </para>
/// </summary>
public interface ICustomerDirectory
{
    /// <summary>
    /// Referência a um cliente. Os consumidores guardam o identificador e lêem
    /// os atributos por aqui.
    ///
    /// <para>
    /// <strong>Não copiam o nome nem o NIF para as suas tabelas</strong> — essas
    /// cópias ficam obsoletas em silêncio (BR-18), e num documento fiscal a
    /// cópia obsoleta é a que fica no ficheiro de auditoria.
    /// </para>
    /// </summary>
    Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// O cliente ligado a esta conta de `identity`, se existir. Resolve "o
    /// próprio" para o Portal do Cliente (ADR-043) — mesmo sentido de leitura
    /// de <c>IEmployeeDirectory.FindByUserIdAsync</c> (ADR-042). Nunca
    /// devolve mais de um cliente por conta: `Customer.UserId` é único
    /// quando preenchido.
    /// </summary>
    Task<CustomerReference?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}

/// <param name="TaxId">NIF. É o que identifica o cliente perante a AGT.</param>
/// <param name="AssignedToEmployeeId">
/// O vendedor responsável, como Colaborador de `hr` — resolvido pelo
/// contrato de `hr`, nunca copiado. Nulo quando ninguém foi atribuído
/// (ADR-045).
/// </param>
public sealed record CustomerReference(
    Guid CustomerId,
    string Name,
    string TaxId,
    CustomerStatus Status,
    BillingAddress BillingAddress,
    Guid? AssignedToEmployeeId = null);

/// <param name="Country">ISO 3166-1 alpha-2. `AO` para Angola.</param>
public sealed record BillingAddress(string Detail, string City, string Country);

public enum CustomerStatus
{
    Active,
    Inactive,
}

/// <summary>Catálogo de permissões de `commercial`, declarado pelo próprio módulo.</summary>
public static class CommercialPermissions
{
    public const string CustomersRead = "commercial.customers.read";

    public const string CustomersWrite = "commercial.customers.write";

    public static readonly IReadOnlyList<string> All = [CustomersRead, CustomersWrite];
}
