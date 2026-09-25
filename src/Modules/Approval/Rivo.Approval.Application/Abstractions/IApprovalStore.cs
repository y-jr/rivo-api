using Rivo.Approval.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Approval.Application.Abstractions;

/// <summary>
/// Persistência de `approval`. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface IApprovalStore
{
    /// <summary>
    /// Políticas activas de um tipo de processo, <strong>com os passos</strong>.
    ///
    /// <para>
    /// Os passos vêm carregados porque a submissão precisa deles para resolver
    /// aprovadores — trazer a política sem eles daria um processo sem passos, e
    /// a recusa apareceria como "nenhum aprovador resolvido", que aponta para o
    /// sítio errado.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesForProcessAsync(
        string processType,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesAsync(CancellationToken cancellationToken);

    Task AddPolicyAsync(ApprovalPolicy policy, CancellationToken cancellationToken);

    Task<ApprovalPolicy?> FindPolicyAsync(Guid policyId, CancellationToken cancellationToken);

    /// <summary>
    /// Um pedido, com atribuições e decisões. Rastreado — quem o procura vai
    /// decidir, e a verificação de BR-4 precisa de ver as decisões anteriores.
    /// </summary>
    Task<ApprovalRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="pagina"/> nulo devolve tudo, como antes de existir
    /// paginação (ADR-068) — o total só vem preenchido quando há página.
    /// </summary>
    Task<(IReadOnlyList<ApprovalRequest> Items, int? TotalCount)> ListRequestsAsync(
        string? processType,
        Guid? pendingForEmployeeId,
        PageRequest? pagina,
        CancellationToken cancellationToken);

    Task AddRequestAsync(ApprovalRequest request, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
