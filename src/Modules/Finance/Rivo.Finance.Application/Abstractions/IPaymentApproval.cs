namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Governança de decisões, <strong>nas palavras de `finance`</strong>.
///
/// <para>
/// Mesma inversão que `hr` usa (ADR-034): `finance` declara o que precisa e
/// não sabe que `approval` existe; quem os apresenta é o composition root.
/// </para>
///
/// <para>
/// <strong>Aqui a inversão não é só higiene — é necessária.</strong>
/// `modules/approval.md` diz que `approval` lê `finance` para o disponível
/// orçamental de BR-8. Se `finance` referenciasse `Rivo.Approval.Contracts`, o
/// dia em que BR-8 for implementada traria de volta o ciclo que o ADR-034
/// fechou. Assim não traz.
/// </para>
/// </summary>
public interface IPaymentApproval
{
    /// <summary>
    /// Falso quando não há motor de governança ligado neste ambiente.
    ///
    /// <para>
    /// Sem governança, um pedido de pagamento <strong>não se cria</strong>.
    /// BR-1 não admite pagamento sem decisão registada, e um pedido que nunca
    /// pudesse ser aprovado seria dívida a fingir que está a caminho.
    /// </para>
    /// </summary>
    bool IsAvailable { get; }

    /// <param name="budgetReference">
    /// A rubrica contra que BR-8 verifica — o centro de custo, em texto.
    /// Atravessa a governança <strong>sem ser interpretada</strong> e volta a
    /// `finance` na verificação orçamental.
    /// </param>
    Task<PaymentApprovalSubmissionResult> SubmitAsync(
        Guid paymentRequestId,
        Guid requestedByEmployeeId,
        decimal amount,
        string currency,
        Guid? departmentId,
        string? budgetReference,
        string summary,
        CancellationToken cancellationToken);

    /// <summary>
    /// Estado corrente e <strong>quem decidiu</strong>.
    ///
    /// <para>
    /// A lista de decisores não é informação decorativa: é o que torna BR-3
    /// verificável. "Quem aprova não paga" só se pode impor sabendo quem
    /// aprovou, e quem aprovou é `approval` que sabe.
    /// </para>
    /// </summary>
    Task<PaymentApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

public sealed record PaymentApprovalSubmissionResult(bool Submitted, Guid? RequestId, string? Reason)
{
    public static PaymentApprovalSubmissionResult Success(Guid requestId) => new(true, requestId, null);

    public static PaymentApprovalSubmissionResult Failed(string reason) => new(false, null, reason);
}

/// <param name="DecidedByEmployeeIds">
/// Todos os que intervieram, aprovando ou não. É contra esta lista que BR-3 é
/// verificada no momento da execução.
/// </param>
public sealed record PaymentApprovalState(
    PaymentApprovalStatus Status,
    IReadOnlyList<Guid> DecidedByEmployeeIds);

public enum PaymentApprovalStatus
{
    /// <summary>Ainda em curso. Não se paga.</summary>
    Pending,

    Approved,

    /// <summary>Rejeitado ou cancelado. Nos dois casos não se paga.</summary>
    Refused,

    /// <summary>
    /// O processo não foi encontrado. <strong>Não se paga por omissão</strong> —
    /// a ausência de decisão não é aprovação.
    /// </summary>
    Unknown,
}
