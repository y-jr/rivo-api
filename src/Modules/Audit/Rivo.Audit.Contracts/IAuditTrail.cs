namespace Rivo.Audit.Contracts;

/// <summary>
/// Superfície publicada do módulo `audit`. É o único tipo que outros módulos
/// referenciam para registar acções.
///
/// <para>
/// O assembly de contratos não depende de nada — nem sequer de `identity`.
/// O actor é apenas um <see cref="Guid"/>: `audit` regista quem agiu, não
/// resolve quem essa pessoa é. É isso que evita um ciclo de dependências
/// entre os dois módulos.
/// </para>
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Regista uma acção. A escrita é síncrona e as falhas propagam-se
    /// deliberadamente: perder um registo de auditoria em silêncio é pior do
    /// que falhar a operação que o originou.
    /// </summary>
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <param name="Action">
/// O que aconteceu, em <see cref="AuditActions"/>. Formato
/// "modulo.recurso.operacao", igual ao das permissões.
/// </param>
/// <param name="EntityType">Tipo do registo afectado, ex.: "identity.user".</param>
/// <param name="EntityId">Identificador do registo afectado.</param>
/// <param name="Context">Actor e origem do pedido.</param>
/// <param name="PreviousValue">Estado anterior, em JSON. Nulo quando não se aplica.</param>
/// <param name="NewValue">Estado novo, em JSON. Nulo quando não se aplica.</param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string EntityId,
    AuditContext Context,
    string? PreviousValue = null,
    string? NewValue = null);

/// <summary>
/// Quem agiu e de onde. Construído na camada API, que é a única que conhece
/// o transporte, e passado até ao ponto de registo.
/// </summary>
/// <param name="ActorId">
/// Nulo em acções sem utilizador autenticado — registo de conta, tentativa de
/// login falhada, ou processos automáticos.
/// </param>
/// <param name="IpAddress">
/// Endereço de origem. ⚠ Atrás de proxy ou em container é o do proxy, não o
/// do cliente — defeito K8 em state/known-issues.md, por corrigir.
/// </param>
/// <param name="CorrelationId">Liga acções do mesmo pedido entre módulos.</param>
public sealed record AuditContext(Guid? ActorId, string? IpAddress, string? CorrelationId);

/// <summary>
/// Acções auditadas. Constantes em vez de texto livre, para que a trilha seja
/// pesquisável e para que uma gralha não crie uma acção nova em silêncio.
/// </summary>
public static class AuditActions
{
    public const string UserRegistered = "identity.user.registered";
    public const string UserLoggedIn = "identity.user.logged_in";
    public const string UserLoginFailed = "identity.user.login_failed";
    public const string UserLoggedOut = "identity.user.logged_out";
    public const string ProfileAssigned = "identity.user.profile_assigned";
}

/// <summary>
/// Tipos de entidade referenciados na trilha. Referência textual e sem chave
/// estrangeira, por desenho: a trilha tem de sobreviver à eliminação lógica do
/// registo que descreve.
/// </summary>
public static class AuditEntityTypes
{
    public const string User = "identity.user";
}

/// <summary>Catálogo de permissões de `audit`, declarado pelo próprio módulo.</summary>
public static class AuditPermissions
{
    public const string TrailRead = "audit.trail.read";

    public static readonly IReadOnlyList<string> All = [TrailRead];
}
