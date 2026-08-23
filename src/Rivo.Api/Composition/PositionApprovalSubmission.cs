using Rivo.Approval.Contracts;
using Rivo.Hr.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a necessidade de `hr` ao motor de `approval`.
///
/// <para>
/// <strong>Vive no host, e é isso que fecha o ciclo.</strong> O ADR-015 §R1
/// previa resolvê-lo com assemblies de contratos dos dois lados — o que resolve
/// a compilação, mas deixa `hr → approval` e `approval → hr` no grafo de
/// módulos, que o teste <c>Modules_HaveNoDependencyCycles</c> continua a ver, e
/// com razão: dois módulos que se lêem mutuamente estão acoplados, compilem ou
/// não.
/// </para>
///
/// <para>
/// A inversão resolve-o de verdade. `hr` declara
/// <see cref="IPositionApprovalSubmission"/> nas suas próprias palavras e não
/// sabe que `approval` existe; quem os apresenta é o composition root, que é o
/// único sítio autorizado a conhecer implementações de todos os módulos
/// (architecture/dependency-rules.md §API).
/// </para>
///
/// <para>
/// Esta classe não decide nada — traduz. Toda a regra continua no domínio de
/// `approval` (ADR-008) e no de `hr` (BR-20).
/// </para>
/// </summary>
public sealed class PositionApprovalSubmission(IApprovalGateway gateway) : IPositionApprovalSubmission
{
    /// <summary>
    /// Verdadeiro sempre que o módulo esteja registado — que é o caso enquanto
    /// o host o compuser. Existe para que `hr` possa correr sem governança sem
    /// que isso seja um caso especial no seu código.
    /// </summary>
    public bool IsAvailable => true;

    public async Task<PositionApprovalSubmissionResult> SubmitAsync(
        Guid assignmentId,
        Guid employeeId,
        Guid positionId,
        string positionName,
        Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var result = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ApprovalProcessTypes.PositionAssignment,
                SourceModule: "hr",

                // A referência à origem é a atribuição, e não o colaborador: é
                // ela que o processo decide, e é por ela que `hr` volta a
                // encontrar o processo depois.
                SourceReference: assignmentId.ToString(),

                RequestedByEmployeeId: employeeId,

                // Uma atribuição de cargo não tem valor monetário, e por isso
                // não cai em faixa de alçada nenhuma (ADR-034).
                Amount: null,
                Currency: null,
                DepartmentId: departmentId,
                Summary: $"Atribuição do cargo '{positionName}', que confere autoridade de aprovação."),
            cancellationToken);

        return result.Outcome == SubmissionOutcome.Submitted
            ? PositionApprovalSubmissionResult.Success(result.RequestId!.Value)
            : PositionApprovalSubmissionResult.Failed(
                result.Reason ?? $"A governança recusou a submissão ({result.Outcome}).");
    }

    public async Task<PositionApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (status is null)
        {
            return PositionApprovalState.Unknown;
        }

        return status.Status switch
        {
            "Approved" => PositionApprovalState.Approved,

            // Rejeitado e cancelado dão no mesmo para `hr`: a atribuição não
            // produz efeito. A distinção interessa a quem lê a trilha de
            // `approval`, não a quem espera pelo efeito.
            "Rejected" or "Cancelled" => PositionApprovalState.Refused,

            _ => PositionApprovalState.Pending,
        };
    }
}
