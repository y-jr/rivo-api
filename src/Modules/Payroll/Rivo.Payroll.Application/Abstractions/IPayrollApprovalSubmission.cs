namespace Rivo.Payroll.Application.Abstractions;

/// <summary>
/// Governança de decisões, <strong>nas palavras de `payroll`</strong>.
///
/// <para>
/// Mesma inversão que `hr` fez em <c>IHrApprovalSubmission</c> e
/// `procurement` em <c>IProcurementApprovalSubmission</c>, e pela mesma razão:
/// `payroll` declara o que precisa, e quem o satisfaz — falando com
/// `approval` — é o composition root, o único sítio autorizado a conhecer
/// implementações de todos os módulos.
/// </para>
/// </summary>
public interface IPayrollApprovalSubmission
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente. Sem
    /// governança, a folha não é submetida: fica em rascunho.
    /// </summary>
    bool IsAvailable { get; }

    Task<PayrollApprovalSubmissionResult> SubmitAsync(
        Guid runId,
        Guid requestedByEmployeeId,
        decimal totalGross,
        string summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente de um processo. `payroll` pergunta; `approval` nunca
    /// empurra — o efeito é aplicado deste lado, por
    /// <c>PayrollRun.MarkApproved</c>/<c>MarkRefused</c>.
    /// </summary>
    Task<PayrollApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed record PayrollApprovalSubmissionResult(bool Submitted, Guid? RequestId, string? Reason)
{
    public static PayrollApprovalSubmissionResult Success(Guid requestId) => new(true, requestId, null);

    public static PayrollApprovalSubmissionResult Failed(string reason) => new(false, null, reason);
}

public enum PayrollApprovalState
{
    Pending,
    Approved,

    /// <summary>Rejeitado ou cancelado — nos dois casos a folha não fica pronta.</summary>
    Refused,

    /// <summary>
    /// O processo não foi encontrado. Por omissão não se aprova — a ausência
    /// de decisão nunca é decisão favorável.
    /// </summary>
    Unknown,
}
