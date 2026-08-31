using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Contracts;

namespace Rivo.Approval.Application;

/// <summary>
/// O contrato publicado de `approval` para composição administrativa
/// (ADR-041). Não reutiliza <c>ListApprovalPolicies</c> — essa devolve
/// <c>PolicyView</c> com passos e aprovadores, mais do que uma vista de
/// configuração precisa de mostrar.
/// </summary>
public sealed class ApprovalPolicyCatalogue(IApprovalStore store) : IApprovalPolicyCatalogue
{
    public async Task<IReadOnlyList<ApprovalPolicySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var policies = await store.ListPoliciesAsync(cancellationToken);

        return [.. policies.Select(p => new ApprovalPolicySummary(
            p.Id, p.ProcessType, p.IsActive, p.Steps.Count, p.RequiresBudgetCheck))];
    }
}
