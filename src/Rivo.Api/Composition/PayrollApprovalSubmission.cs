using Rivo.Approval.Contracts;
using Rivo.Payroll.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a necessidade de `payroll` ao motor de `approval`.
///
/// <para>
/// Mesmo desenho de <see cref="ProcurementApprovalSubmission"/>, e vive aqui
/// pela mesma razão: o composition root é o único sítio autorizado a conhecer
/// implementações de todos os módulos (architecture/dependency-rules.md
/// §API). Sem ciclo para quebrar — `approval` não lê `payroll`.
/// </para>
///
/// <para>
/// Esta classe não decide nada — traduz. O bruto da folha (sem IRT/INSS, ver
/// `PayrollRun`) é o único número que tem para dar à política de aprovação.
/// </para>
/// </summary>
public sealed class PayrollApprovalSubmission(IApprovalGateway gateway) : IPayrollApprovalSubmission
{
    public bool IsAvailable => true;

    public async Task<PayrollApprovalSubmissionResult> SubmitAsync(
        Guid runId,
        Guid requestedByEmployeeId,
        decimal totalGross,
        string summary,
        CancellationToken cancellationToken)
    {
        var result = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ApprovalProcessTypes.PayrollRun,
                SourceModule: "payroll",
                SourceReference: runId.ToString(),
                RequestedByEmployeeId: requestedByEmployeeId,
                Amount: totalGross,
                Currency: "AOA",
                DepartmentId: null,
                Summary: summary,
                BudgetReference: null),
            cancellationToken);

        return result.Outcome == SubmissionOutcome.Submitted
            ? PayrollApprovalSubmissionResult.Success(result.RequestId!.Value)
            : PayrollApprovalSubmissionResult.Failed(
                result.Reason ?? $"A governança recusou a submissão ({result.Outcome}).");
    }

    public async Task<PayrollApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (status is null)
        {
            return PayrollApprovalState.Unknown;
        }

        return status.Status switch
        {
            "Approved" => PayrollApprovalState.Approved,
            "Rejected" or "Cancelled" => PayrollApprovalState.Refused,
            _ => PayrollApprovalState.Pending,
        };
    }
}
