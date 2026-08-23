namespace Rivo.Hr.Application.Abstractions;

/// <summary>
/// Processos de `hr` que precisam de decisão antes de produzir efeito.
///
/// <para>
/// <strong>Vocabulário de `hr`, não de `approval`.</strong> O motor de
/// governança tem os seus próprios identificadores de processo; a
/// correspondência entre os dois é feita no composition root, que é o único
/// sítio que conhece ambos. Assim `hr` não precisa de referenciar
/// `Rivo.Approval.Contracts` — e o ciclo `hr ↔ approval` não se forma.
/// </para>
/// </summary>
public enum HrApprovalProcess
{
    /// <summary>
    /// Atribuição de um Cargo que confere autoridade de aprovação (BR-20).
    /// É o único processo cujo resultado altera quem pode aprovar no futuro.
    /// </summary>
    PositionAssignment,

    /// <summary>Pedido de férias.</summary>
    LeaveRequest,
}

/// <summary>
/// Governança de decisões, <strong>nas palavras de `hr`</strong>.
///
/// <para>
/// <strong>Porque é que isto existe em vez de `hr` referenciar
/// <c>Rivo.Approval.Contracts</c>:</strong> o ADR-015 §R1 previa resolver o
/// ciclo <c>hr ↔ approval</c> com assemblies de contratos de ambos os lados —
/// e isso resolve a <em>compilação</em>, mas não o desenho. O teste
/// <c>Modules_HaveNoDependencyCycles</c> (ADR-024) continua a ver um ciclo,
/// porque continua a haver um: dois módulos que se leem mutuamente estão
/// acoplados, compilem ou não.
/// </para>
///
/// <para>
/// A inversão fecha-o de verdade. `hr` declara o que precisa; quem o
/// satisfaz — falando com `approval` — é o composition root, que é o único
/// sítio autorizado a conhecer implementações de todos os módulos
/// (architecture/dependency-rules.md §API).
/// </para>
///
/// <para>
/// Consequência prática: <strong>`hr` não sabe que `approval` existe.</strong>
/// Trocar o motor de governança, ou correr `hr` sem nenhum, não obriga a tocar
/// em `hr`.
/// </para>
/// </summary>
public interface IHrApprovalSubmission
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente.
    ///
    /// <para>
    /// Distinto de "a submissão falhou": sem governança, `hr` recusa o processo
    /// com <c>501</c> em vez de o deixar passar. Melhor não produzir o efeito
    /// do que produzi-lo sem quem o aprove.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Submete um processo a decisão e devolve o identificador do processo de
    /// aprovação.
    /// </summary>
    /// <param name="sourceReference">
    /// Identificador do registo de `hr` que o processo decide — a atribuição,
    /// o pedido de férias. É por ele que `hr` reencontra o processo depois.
    /// </param>
    Task<HrApprovalSubmissionResult> SubmitAsync(
        HrApprovalProcess process,
        Guid sourceReference,
        Guid requestedByEmployeeId,
        Guid? departmentId,
        string summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente de um processo.
    ///
    /// <para>
    /// <strong>`hr` pergunta; `approval` nunca empurra.</strong>
    /// `modules/approval.md` proíbe expressamente que `approval` modifique
    /// dados de negócio do módulo de origem — o efeito tem de partir daqui.
    /// </para>
    /// </summary>
    Task<HrApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed record HrApprovalSubmissionResult(bool Submitted, Guid? RequestId, string? Reason)
{
    public static HrApprovalSubmissionResult Success(Guid requestId) => new(true, requestId, null);

    public static HrApprovalSubmissionResult Failed(string reason) => new(false, null, reason);
}

public enum HrApprovalState
{
    /// <summary>Ainda em curso. O efeito continua retido.</summary>
    Pending,

    Approved,

    /// <summary>Rejeitado ou cancelado — nos dois casos o efeito não se produz.</summary>
    Refused,

    /// <summary>O processo não foi encontrado. Não se produz efeito por omissão.</summary>
    Unknown,
}
