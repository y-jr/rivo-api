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
    /// <summary>
    /// ⚠ Histórico. O registo público fechou a 2026-09-13 (ADR-059): quem tem
    /// conta passa a ser quem foi convidado. A constante fica porque a trilha
    /// é append-only (BR-14) e as entradas antigas continuam a referenciá-la.
    /// </summary>
    public const string UserRegistered = "identity.user.registered";

    /// <summary>Alguém com autoridade abriu uma conta e convidou o titular (ADR-059).</summary>
    public const string UserInvited = "identity.user.invited";

    /// <summary>O convidado escolheu password, e a conta passou a poder entrar.</summary>
    public const string InvitationAccepted = "identity.user.invitation_accepted";
    public const string UserLoggedIn = "identity.user.logged_in";
    public const string UserLoginFailed = "identity.user.login_failed";
    public const string UserLoggedOut = "identity.user.logged_out";
    public const string ProfileAssigned = "identity.user.profile_assigned";
    public const string ProfileRemoved = "identity.user.profile_removed";

    /// <summary>O próprio mudou a sua password.</summary>
    public const string PasswordChanged = "identity.user.password_changed";

    /// <summary>
    /// Tentou mudar a password e falhou a actual. Registada porque uma
    /// sequência delas é a assinatura de quem tem o token e não a credencial.
    /// </summary>
    public const string PasswordChangeRefused = "identity.user.password_change_refused";

    /// <summary>
    /// Um administrador repôs a password de outra conta. <strong>Acção própria
    /// e não uma mudança qualquer</strong>: é o caminho por onde uma conta é
    /// tomada, e quem audita tem de o encontrar sem o procurar no meio das
    /// mudanças legítimas.
    /// </summary>
    public const string PasswordReset = "identity.user.password_reset";

    /// <summary>
    /// Alguém pediu a recuperação da sua password por correio (ADR-065).
    ///
    /// <para>
    /// **Registado mesmo quando o endereço não tem conta** — aí o
    /// <c>entity_id</c> é o próprio endereço tentado, porque não há
    /// identificador que registar. Uma sequência destes contra endereços
    /// diferentes é exactamente o que se quer poder ver depois, e é a única
    /// pista que sobra de uma rota que responde sempre o mesmo a quem a chama.
    /// </para>
    /// </summary>
    public const string PasswordRecoveryRequested = "identity.user.password_recovery_requested";

    /// <summary>A recuperação chegou ao fim: a password foi mudada por quem recebeu o testemunho.</summary>
    public const string PasswordRecoveryCompleted = "identity.user.password_recovery_completed";

    /// <summary>
    /// Testemunho de recuperação recusado — já usado, expirado, ou adivinhado.
    /// Fica na trilha por ser o rasto de uma tentativa que não passou.
    /// </summary>
    public const string PasswordRecoveryFailed = "identity.user.password_recovery_failed";

    public const string AccountDeactivated = "identity.user.deactivated";
    public const string AccountReactivated = "identity.user.reactivated";

    /// <summary>
    /// Uma identidade de provider externo passou a poder entrar nesta conta
    /// (ADR-032). Acção própria, e não um login qualquer: a conta ganhou um
    /// caminho de credencial novo, o que é alteração de estado com peso de
    /// segurança. Acontece uma vez por pessoa e por provider.
    /// </summary>
    public const string ExternalLoginLinked = "identity.user.external_login_linked";
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
