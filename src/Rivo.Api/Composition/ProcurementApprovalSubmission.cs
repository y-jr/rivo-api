using Rivo.Approval.Contracts;
using Rivo.Procurement.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a necessidade de `procurement` ao motor de `approval`.
///
/// <para>
/// Mesmo desenho de <see cref="HrApprovalSubmission"/>, e vive aqui pela mesma
/// razão: o composition root é o único sítio autorizado a conhecer
/// implementações de todos os módulos
/// (architecture/dependency-rules.md §API).
/// </para>
///
/// <para>
/// <strong>Aqui não há ciclo para quebrar</strong> — `approval` não lê
/// `procurement`, e uma referência directa compilaria. Mantém-se a inversão
/// para preservar a propriedade que o ADR-034 comprou: `procurement` não sabe
/// qual é o motor de governança, e trocá-lo não lhe toca no código.
/// </para>
///
/// <para>
/// Esta classe não decide nada — traduz.
/// </para>
/// </summary>
public sealed class ProcurementApprovalSubmission(IApprovalGateway gateway)
    : IProcurementApprovalSubmission
{
    public bool IsAvailable => true;

    public async Task<ProcurementApprovalSubmissionResult> SubmitAsync(
        Guid requisitionId,
        Guid requestedByEmployeeId,
        Guid? departmentId,
        decimal estimatedTotal,
        string currency,
        string summary,
        CancellationToken cancellationToken)
    {
        var result = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ApprovalProcessTypes.PurchaseRequisition,
                SourceModule: "procurement",

                // A requisição, e não o requisitante: é o registo que o processo
                // decide, e é por ele que `procurement` o reencontra.
                SourceReference: requisitionId.ToString(),

                RequestedByEmployeeId: requestedByEmployeeId,

                // O valor estimado escolhe a faixa da alçada. É estimativa, e a
                // política que a usar tem de saber disso — a factura pode vir
                // acima, e quem a registar já não passa por aqui.
                Amount: estimatedTotal,
                Currency: currency,
                DepartmentId: departmentId,
                Summary: summary,

                // Sem rubrica: `procurement` não tem noção de centro de custo, e
                // inventar uma seria pior do que não a dar. A verificação
                // orçamental de BR-8 recua para o departamento, que é o que um
                // módulo sem essa noção consegue oferecer.
                BudgetReference: null),
            cancellationToken);

        return result.Outcome == SubmissionOutcome.Submitted
            ? ProcurementApprovalSubmissionResult.Success(result.RequestId!.Value)
            : ProcurementApprovalSubmissionResult.Failed(
                result.Reason ?? $"A governança recusou a submissão ({result.Outcome}).");
    }

    public async Task<ProcurementApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (status is null)
        {
            return ProcurementApprovalState.Unknown;
        }

        return status.Status switch
        {
            "Approved" => ProcurementApprovalState.Approved,

            // Rejeitado e cancelado dão no mesmo aqui: não se compra. A
            // distinção interessa a quem lê a trilha de `approval`.
            "Rejected" or "Cancelled" => ProcurementApprovalState.Refused,

            _ => ProcurementApprovalState.Pending,
        };
    }
}
