namespace Rivo.Hr.Application.Abstractions;

/// <summary>
/// Governança de decisões para atribuições de Cargo, <strong>nas palavras de
/// `hr`</strong>.
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
public interface IPositionApprovalSubmission
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente.
    ///
    /// <para>
    /// Distinto de "a submissão falhou": sem governança, `hr` volta a recusar a
    /// atribuição com <c>501</c>, que é o comportamento anterior ao ADR-034 e
    /// continua a ser o correcto — melhor não atribuir do que atribuir
    /// autoridade sem quem a aprove (BR-20).
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Submete a atribuição a decisão e devolve o identificador do processo.
    /// </summary>
    Task<PositionApprovalSubmissionResult> SubmitAsync(
        Guid assignmentId,
        Guid employeeId,
        Guid positionId,
        string positionName,
        Guid? departmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente do processo que governa uma atribuição.
    ///
    /// <para>
    /// <strong>`hr` pergunta; `approval` nunca empurra.</strong>
    /// `modules/approval.md` proíbe expressamente que `approval` modifique
    /// dados de negócio do módulo de origem — a promoção da atribuição a
    /// efectiva tem de partir daqui.
    /// </para>
    /// </summary>
    Task<PositionApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed record PositionApprovalSubmissionResult(bool Submitted, Guid? RequestId, string? Reason)
{
    public static PositionApprovalSubmissionResult Success(Guid requestId) => new(true, requestId, null);

    public static PositionApprovalSubmissionResult Failed(string reason) => new(false, null, reason);
}

public enum PositionApprovalState
{
    /// <summary>Ainda em curso. A atribuição continua sem conferir nada.</summary>
    Pending,

    Approved,

    /// <summary>Rejeitado ou cancelado — nos dois casos a atribuição não produz efeito.</summary>
    Refused,

    /// <summary>O processo não foi encontrado. Não se promove nada por omissão.</summary>
    Unknown,
}
