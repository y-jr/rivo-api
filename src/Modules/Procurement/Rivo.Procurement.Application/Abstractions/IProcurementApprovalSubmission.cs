namespace Rivo.Procurement.Application.Abstractions;

/// <summary>
/// Governança de decisões, <strong>nas palavras de `procurement`</strong>.
///
/// <para>
/// Mesma inversão que `hr` fez em <c>IHrApprovalSubmission</c> e `finance` em
/// <c>IPaymentApproval</c>, e pela mesma razão: `procurement` declara o que
/// precisa, e quem o satisfaz — falando com `approval` — é o composition root,
/// o único sítio autorizado a conhecer implementações de todos os módulos.
/// </para>
///
/// <para>
/// <strong>Aqui a inversão não é para quebrar um ciclo</strong>, como era em
/// `hr ↔ approval`: `approval` não lê `procurement`. É para manter a
/// propriedade que o ADR-034 comprou — que um módulo de negócio não saiba qual
/// é o motor de governança, e continue a funcionar sem nenhum. Referenciar
/// `Rivo.Approval.Contracts` compilaria na mesma; passaria a haver uma
/// direcção declarada que não é precisa, e a próxima seria mais fácil.
/// </para>
/// </summary>
public interface IProcurementApprovalSubmission
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente.
    ///
    /// <para>
    /// Sem governança, a requisição não é submetida: fica em rascunho e o
    /// pedido responde <c>501</c>. Uma requisição "submetida" que ninguém pode
    /// decidir seria pior do que uma recusada — parece estar em curso e não
    /// está.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    Task<ProcurementApprovalSubmissionResult> SubmitAsync(
        Guid requisitionId,
        Guid requestedByEmployeeId,
        Guid? departmentId,
        decimal estimatedTotal,
        string currency,
        string summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente de um processo.
    ///
    /// <para>
    /// <strong>`procurement` pergunta; `approval` nunca empurra.</strong> O
    /// efeito da decisão é aplicado deste lado, por
    /// <c>PurchaseRequisition.MarkApproved</c> — `modules/approval.md` proíbe
    /// que o motor altere dados de negócio do módulo de origem.
    /// </para>
    /// </summary>
    Task<ProcurementApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed record ProcurementApprovalSubmissionResult(bool Submitted, Guid? RequestId, string? Reason)
{
    public static ProcurementApprovalSubmissionResult Success(Guid requestId) => new(true, requestId, null);

    public static ProcurementApprovalSubmissionResult Failed(string reason) => new(false, null, reason);
}

public enum ProcurementApprovalState
{
    /// <summary>Ainda em curso. A requisição continua pendente.</summary>
    Pending,

    Approved,

    /// <summary>Rejeitado ou cancelado — nos dois casos não se compra.</summary>
    Refused,

    /// <summary>
    /// O processo não foi encontrado. <strong>Por omissão não se aprova</strong>
    /// — a ausência de decisão nunca é decisão favorável.
    /// </summary>
    Unknown,
}
