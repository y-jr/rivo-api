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
/// <see cref="IHrApprovalSubmission"/> nas suas próprias palavras e não sabe
/// que `approval` existe; quem os apresenta é o composition root, que é o único
/// sítio autorizado a conhecer implementações de todos os módulos
/// (architecture/dependency-rules.md §API).
/// </para>
///
/// <para>
/// Esta classe não decide nada — traduz. Toda a regra continua no domínio de
/// `approval` (ADR-008) e no de `hr` (BR-20).
/// </para>
/// </summary>
public sealed class HrApprovalSubmission(IApprovalGateway gateway) : IHrApprovalSubmission
{
    /// <summary>
    /// Verdadeiro sempre que o módulo esteja registado — que é o caso enquanto
    /// o host o compuser. Existe para que `hr` possa correr sem governança sem
    /// que isso seja um caso especial no seu código.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Traduz o vocabulário de `hr` para o de `approval`.
    ///
    /// <para>
    /// A correspondência é explícita e verificada pelo compilador dos dois
    /// lados. Um processo novo em `hr` sem entrada aqui não compila — que é
    /// melhor do que passar uma cadeia de caracteres que não corresponde a
    /// política nenhuma e falhar em produção.
    /// </para>
    /// </summary>
    private static string ToApprovalProcessType(HrApprovalProcess process) => process switch
    {
        HrApprovalProcess.PositionAssignment => ApprovalProcessTypes.PositionAssignment,
        HrApprovalProcess.LeaveRequest => ApprovalProcessTypes.LeaveRequest,
        _ => throw new ArgumentOutOfRangeException(nameof(process)),
    };

    public async Task<HrApprovalSubmissionResult> SubmitAsync(
        HrApprovalProcess process,
        Guid sourceReference,
        Guid requestedByEmployeeId,
        Guid? departmentId,
        string summary,
        CancellationToken cancellationToken)
    {
        var result = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ToApprovalProcessType(process),
                SourceModule: "hr",

                // A referência à origem é o registo que o processo decide — a
                // atribuição, o pedido de férias — e não o colaborador. É por
                // ela que `hr` reencontra o processo depois.
                SourceReference: sourceReference.ToString(),

                RequestedByEmployeeId: requestedByEmployeeId,

                // Nenhum processo de `hr` tem valor monetário, e por isso não
                // cai em faixa de alçada nenhuma (ADR-034).
                Amount: null,
                Currency: null,
                DepartmentId: departmentId,
                Summary: summary),
            cancellationToken);

        return result.Outcome == SubmissionOutcome.Submitted
            ? HrApprovalSubmissionResult.Success(result.RequestId!.Value)
            : HrApprovalSubmissionResult.Failed(
                result.Reason ?? $"A governança recusou a submissão ({result.Outcome}).");
    }

    public async Task<HrApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (status is null)
        {
            return HrApprovalState.Unknown;
        }

        return status.Status switch
        {
            "Approved" => HrApprovalState.Approved,

            // Rejeitado e cancelado dão no mesmo para `hr`: o efeito não se
            // produz. A distinção interessa a quem lê a trilha de `approval`,
            // não a quem espera pelo efeito.
            "Rejected" or "Cancelled" => HrApprovalState.Refused,

            _ => HrApprovalState.Pending,
        };
    }
}
