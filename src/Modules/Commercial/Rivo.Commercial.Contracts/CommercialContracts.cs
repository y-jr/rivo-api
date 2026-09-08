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

    /// <summary>
    /// Todos os clientes, para quem tem de os enumerar em vez de os procurar um
    /// a um. Primeiro consumidor: a secção <c>Customer</c> do SAF-T AO.
    ///
    /// <para>
    /// <strong>Inclui os inactivos, e não é opção.</strong> Um cliente
    /// desactivado hoje pode ter sido facturado em Março, e o XSD exige que
    /// todo o documento referencie master data presente no ficheiro
    /// (<c>CustomerIDConstraint</c>). Deixar de fora quem já não vende seria
    /// produzir um ficheiro com referências penduradas — e a desactivação
    /// existe exactamente porque BR-14 proíbe eliminar quem tem documentos
    /// emitidos.
    /// </para>
    ///
    /// <para>
    /// Sem paginação, de propósito: quem exporta um SAF-T precisa do conjunto
    /// inteiro, e um ficheiro parcial não é um ficheiro válido. Se a carteira
    /// crescer ao ponto de isto não caber em memória, a resposta é
    /// <em>streaming</em> na exportação, não uma página que quem chama tem de
    /// se lembrar de percorrer até ao fim.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CustomerReference>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Regista um cliente — escrita através do contrato, mesmo padrão de
    /// <c>ICustomerMessaging</c>/<c>ICustomerPayments</c> (ADR-044/ADR-045).
    /// Primeiro consumidor: a importação em massa via CSV de
    /// `Rivo.Settings` (Analytics & IA, ADR-047) — mesma validação e mesma
    /// verificação de NIF duplicado do caso de uso interno.
    /// </summary>
    /// <param name="actorId">Quem importou, para a trilha de auditoria — nunca <c>AuditContext</c> através do contrato (ADR-017, sem dependências).</param>
    Task<CustomerRegistrationResult> RegisterAsync(
        string name,
        string taxId,
        string addressDetail,
        string city,
        string country,
        string? email,
        string? phone,
        Guid actorId,
        CancellationToken cancellationToken);
}

public sealed record CustomerRegistrationResult(CustomerRegistrationOutcome Outcome, Guid? CustomerId, string? Error)
{
    public static CustomerRegistrationResult Success(Guid customerId) =>
        new(CustomerRegistrationOutcome.Registered, customerId, null);

    public static CustomerRegistrationResult Rejected(string error) =>
        new(CustomerRegistrationOutcome.Rejected, null, error);

    /// <param name="existingId">Devolvido de propósito — ver <c>RegisterCustomerResult.Duplicate</c>, mesma razão.</param>
    public static CustomerRegistrationResult Duplicate(Guid existingId) =>
        new(CustomerRegistrationOutcome.DuplicateTaxId, existingId, null);
}

public enum CustomerRegistrationOutcome
{
    Registered,
    Rejected,
    DuplicateTaxId,
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
