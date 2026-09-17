using Rivo.Approval.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Inventory.Application.Abstractions;

namespace Rivo.Api.Composition;

/// <summary>
/// Liga a necessidade de `inventory` ao motor de `approval` (ADR-064).
///
/// <para>
/// Mesmo desenho de <see cref="PayrollApprovalSubmission"/>, e vive aqui pela
/// mesma razão: o composition root é o único sítio autorizado a conhecer
/// implementações de todos os módulos.
/// </para>
///
/// <para>
/// <strong>A tradução que interessa é a de <c>NoApplicablePolicy</c>.</strong>
/// Para `payroll` essa resposta é uma falha — uma folha salarial aprova-se
/// sempre, e não haver política é configuração em falta. Para uma contagem é o
/// contrário: significa que nenhuma alçada configurada cobre esta divergência,
/// e portanto que ninguém precisa de olhar para ela. O mesmo desfecho do
/// contrato de `approval`, lido de duas maneiras diferentes por dois módulos
/// com necessidades diferentes — que é exactamente o que um contrato partilhado
/// deve permitir.
/// </para>
/// </summary>
public sealed class InventoryApprovalSubmission(
    IApprovalGateway gateway,
    IEmployeeDirectory employees,
    TimeProvider clock) : IInventoryApprovalSubmission
{
    public bool IsAvailable => true;

    public async Task<InventoryApprovalSubmissionResult> SubmitAsync(
        Guid countId,
        Guid requestedByUserId,
        decimal varianceValue,
        string summary,
        CancellationToken cancellationToken)
    {
        // Quem requer resolve-se do vínculo, nunca de um identificador
        // declarado (ADR-057). Sem colaborador ligado não há contra quem
        // verificar a segregação de funções, e a submissão fica bloqueada — não
        // se aplica a correcção em silêncio só porque quem fechou não tem ficha.
        var colaborador = await employees.FindByUserIdAsync(
            requestedByUserId, clock.GetUtcNow(), cancellationToken);

        if (colaborador is null)
        {
            return InventoryApprovalSubmissionResult.Blocked(
                "Esta conta não está associada a nenhum colaborador, e uma divergência de "
                + "inventário só se submete a decisão em nome de quem a encontrou.");
        }

        var result = await gateway.SubmitAsync(
            new ApprovalSubmission(
                ApprovalProcessTypes.StockCount,
                SourceModule: "inventory",
                SourceReference: countId.ToString(),
                RequestedByEmployeeId: colaborador.EmployeeId,
                Amount: varianceValue,
                Currency: "AOA",
                DepartmentId: null,
                Summary: summary,

                // Uma divergência de inventário não consome orçamento: é uma
                // perda ou um ganho já acontecido, que se regista. Verificar
                // orçamento aqui seria perguntar se há dinheiro para uma coisa
                // que já sucedeu.
                BudgetReference: null),
            cancellationToken);

        return result.Outcome switch
        {
            SubmissionOutcome.Submitted =>
                InventoryApprovalSubmissionResult.Submitted(result.RequestId!.Value),

            SubmissionOutcome.NoApplicablePolicy =>
                InventoryApprovalSubmissionResult.NoApplicablePolicy(result.Reason),

            _ => InventoryApprovalSubmissionResult.Blocked(
                result.Reason ?? $"A governança recusou a submissão ({result.Outcome})."),
        };
    }

    public async Task<InventoryApprovalState> GetStateAsync(
        Guid approvalRequestId,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(approvalRequestId, cancellationToken);

        if (status is null)
        {
            return InventoryApprovalState.Unknown;
        }

        return status.Status switch
        {
            "Approved" => InventoryApprovalState.Approved,
            "Rejected" or "Cancelled" => InventoryApprovalState.Refused,
            _ => InventoryApprovalState.Pending,
        };
    }
}
