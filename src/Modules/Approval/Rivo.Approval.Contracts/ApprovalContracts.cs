namespace Rivo.Approval.Contracts;

/// <summary>
/// Superfície publicada de `approval`. É o que os módulos de negócio
/// referenciam para submeter processos e consultar o resultado.
///
/// <para>
/// <strong>Este assembly não depende de nada</strong>, e é isso que resolve o
/// ciclo <c>hr ↔ approval</c> (ADR-015 §R1, ADR-034): `hr` referencia
/// <c>Rivo.Approval.Contracts</c> e `approval` referencia
/// <c>Rivo.Hr.Contracts</c>, sem que os projectos se referenciem mutuamente.
/// </para>
/// </summary>
public interface IApprovalGateway
{
    /// <summary>
    /// Submete um processo a aprovação.
    ///
    /// <para>
    /// Quem submete <strong>não decide nada</strong> sobre o encaminhamento: a
    /// política aplicável, os passos e os aprovadores concretos são resolvidos
    /// por `approval` no momento da submissão e congelados (BR-6).
    /// </para>
    /// </summary>
    Task<SubmissionResult> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente de um pedido. É por aqui que o módulo de origem sabe se
    /// já pode produzir o efeito que reteve.
    /// </summary>
    Task<ApprovalStatusView?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken);
}

/// <param name="ProcessType">
/// O que se está a aprovar, em <see cref="ApprovalProcessTypes"/>. Determina
/// que políticas se aplicam.
/// </param>
/// <param name="SourceModule">Módulo de origem, ex.: "hr".</param>
/// <param name="SourceReference">
/// Identificador do registo de origem. `approval` guarda-o e devolve-o; **não o
/// interpreta** — não sabe nem pode saber o que significa o que aprova.
/// </param>
/// <param name="RequestedByEmployeeId">
/// Requisitante, como Colaborador. É contra ele que BR-2 é verificada.
/// </param>
/// <param name="Amount">
/// Valor em causa, quando aplicável. Selecciona a faixa da política; nulo em
/// processos sem valor, como uma atribuição de cargo.
/// </param>
/// <param name="DepartmentId">Departamento do processo, para escolher a política.</param>
public sealed record ApprovalSubmission(
    string ProcessType,
    string SourceModule,
    string SourceReference,
    Guid RequestedByEmployeeId,
    decimal? Amount,
    string? Currency,
    Guid? DepartmentId,
    string? Summary = null);

/// <param name="Outcome">
/// <see cref="SubmissionOutcome.BudgetCheckUnavailable"/> não é falha técnica:
/// é a recusa deliberada de processos que exigem verificação orçamental
/// enquanto `finance` não existir (ADR-034).
/// </param>
public sealed record SubmissionResult(SubmissionOutcome Outcome, Guid? RequestId, string? Reason)
{
    public static SubmissionResult Submitted(Guid requestId) =>
        new(SubmissionOutcome.Submitted, requestId, null);

    public static SubmissionResult NoPolicy(string reason) =>
        new(SubmissionOutcome.NoApplicablePolicy, null, reason);

    public static SubmissionResult AmbiguousPolicy(string reason) =>
        new(SubmissionOutcome.AmbiguousPolicy, null, reason);

    public static SubmissionResult NoApprovers(string reason) =>
        new(SubmissionOutcome.NoApproversResolved, null, reason);

    public static SubmissionResult BudgetCheckUnavailable(string reason) =>
        new(SubmissionOutcome.BudgetCheckUnavailable, null, reason);
}

public enum SubmissionOutcome
{
    Submitted,

    /// <summary>Nenhuma política corresponde. Erro de configuração, não do pedido.</summary>
    NoApplicablePolicy,

    /// <summary>
    /// Duas políticas igualmente específicas correspondem. Recusa-se em vez de
    /// escolher ao acaso: significaria que ninguém sabe qual é a alçada.
    /// </summary>
    AmbiguousPolicy,

    /// <summary>
    /// A política aplica-se mas nenhum Cargo dela tem ocupante. Um processo sem
    /// aprovadores nunca sairia de pendente.
    /// </summary>
    NoApproversResolved,

    /// <summary>
    /// A política exige verificação orçamental (BR-8) e `finance` não existe.
    /// Traduz-se em 501 na fronteira HTTP.
    /// </summary>
    BudgetCheckUnavailable,
}

/// <param name="PendingApprovers">
/// Quem falta decidir agora. Vazio quando o processo terminou.
/// </param>
public sealed record ApprovalStatusView(
    Guid RequestId,
    string ProcessType,
    string SourceModule,
    string SourceReference,
    string Status,
    int CurrentStep,
    int TotalSteps,
    IReadOnlyList<Guid> PendingApprovers,
    IReadOnlyList<ApprovalDecisionView> Decisions);

public sealed record ApprovalDecisionView(
    Guid DecidedByEmployeeId,
    string Action,
    DateTimeOffset DecidedAt,
    int Step,
    string? Notes);

/// <summary>
/// Tipos de processo conhecidos. Constantes em vez de texto livre: o valor
/// escolhe a política, e uma gralha faria um processo cair na alçada errada —
/// ou em nenhuma.
/// </summary>
public static class ApprovalProcessTypes
{
    /// <summary>
    /// Atribuição de um Cargo que confere autoridade de aprovação (BR-20).
    /// É o único processo cujo resultado altera quem pode aprovar no futuro.
    /// </summary>
    public const string PositionAssignment = "hr.position_assignment";

    /// <summary>Pedido de férias (BR do módulo `hr`).</summary>
    public const string LeaveRequest = "hr.leave_request";

    public static readonly IReadOnlyList<string> All = [PositionAssignment, LeaveRequest];
}

/// <summary>Catálogo de permissões de `approval`, declarado pelo próprio módulo.</summary>
public static class ApprovalPermissions
{
    public const string RequestsRead = "approval.requests.read";

    /// <summary>
    /// Decidir sobre um pedido. <strong>Não basta ter a permissão</strong> — é
    /// preciso estar atribuído ao passo corrente, e não ser o requisitante
    /// (BR-2, BR-4). A permissão abre a porta; o domínio é que decide.
    /// </summary>
    public const string RequestsDecide = "approval.requests.decide";

    /// <summary>Gerir políticas e alçadas. Configuração sensível: altera quem aprova o quê.</summary>
    public const string PoliciesRead = "approval.policies.read";

    public const string PoliciesWrite = "approval.policies.write";

    public static readonly IReadOnlyList<string> All =
        [RequestsRead, RequestsDecide, PoliciesRead, PoliciesWrite];
}
