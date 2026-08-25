using Rivo.Approval.Contracts;
using Rivo.Finance.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a necessidade de `finance` ao motor de `approval`.
///
/// <para>
/// Mesma inversão que <see cref="HrApprovalSubmission"/> faz para `hr`, e pela
/// mesma razão de fundo — mas aqui com um motivo adicional que não é
/// hipotético: <strong>`modules/approval.md` diz que `approval` lê `finance`
/// para o disponível orçamental de BR-8.</strong> Se `finance` referenciasse
/// `Rivo.Approval.Contracts`, o dia em que BR-8 for implementada traria de volta
/// exactamente o ciclo que o ADR-034 fechou.
/// </para>
///
/// <para>
/// Esta classe não decide nada — traduz. BR-1, BR-3 e BR-5 continuam no domínio
/// de `finance` e na sua camada Application; BR-2, BR-4 e BR-6 continuam no
/// domínio de `approval`.
/// </para>
/// </summary>
public sealed class FinancePaymentApproval(IApprovalGateway gateway) : IPaymentApproval
{
    public bool IsAvailable => true;

    public async Task<PaymentApprovalSubmissionResult> SubmitAsync(
        Guid paymentRequestId,
        Guid requestedByEmployeeId,
        decimal amount,
        string currency,
        Guid? departmentId,
        string summary,
        CancellationToken cancellationToken)
    {
        var resultado = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ApprovalProcessTypes.PaymentRequest,
                SourceModule: "finance",
                SourceReference: paymentRequestId.ToString(),
                requestedByEmployeeId,
                // O valor selecciona a faixa da política — é o que faz uma
                // alçada de 100 000 ser diferente de uma de 10 000.
                amount,
                currency,
                departmentId,
                summary),
            cancellationToken);

        return resultado.Outcome switch
        {
            SubmissionOutcome.Submitted =>
                PaymentApprovalSubmissionResult.Success(resultado.RequestId!.Value),

            _ => PaymentApprovalSubmissionResult.Failed(
                resultado.Reason ?? "A submissão a aprovação foi recusada."),
        };
    }

    public async Task<PaymentApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var estado = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (estado is null)
        {
            // Sem processo não se paga. A ausência de decisão não é aprovação.
            return new PaymentApprovalState(PaymentApprovalStatus.Unknown, []);
        }

        var decisores = estado.Decisions
            .Select(decisao => decisao.DecidedByEmployeeId)
            .Distinct()
            .ToList();

        // A correspondência é por texto porque é assim que `approval` publica o
        // estado. Um valor desconhecido cai em `Refused` e não em `Approved`:
        // por omissão não se paga.
        var status = estado.Status switch
        {
            "Approved" => PaymentApprovalStatus.Approved,
            "Pending" or "InProgress" or "OnHold" or "ClarificationRequested" => PaymentApprovalStatus.Pending,
            _ => PaymentApprovalStatus.Refused,
        };

        return new PaymentApprovalState(status, decisores);
    }
}
