using Microsoft.EntityFrameworkCore;
using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Domain;

namespace Rivo.Approval.Infrastructure.Persistence;

public sealed class ApprovalStore(ApprovalDbContext context) : IApprovalStore
{
    public async Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesForProcessAsync(
        string processType,
        CancellationToken cancellationToken) =>
        await context.Policies
            .AsNoTracking()
            .Include(p => p.Steps)
            .Where(p => p.ProcessType == processType && p.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesAsync(CancellationToken cancellationToken) =>
        await context.Policies
            .AsNoTracking()
            .Include(p => p.Steps)
            .OrderBy(p => p.ProcessType)
            .ToListAsync(cancellationToken);

    public async Task AddPolicyAsync(ApprovalPolicy policy, CancellationToken cancellationToken) =>
        await context.Policies.AddAsync(policy, cancellationToken);

    public async Task<ApprovalPolicy?> FindPolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        await context.Policies
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);

    public async Task<ApprovalRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        // Rastreado e completo: quem o procura vai decidir, e a verificação de
        // BR-4 precisa de ver as decisões anteriores. Sem `Include`, a lista
        // viria vazia e a regra passaria sempre.
        await context.Requests
            .Include(r => r.Assignments)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> ListRequestsAsync(
        string? processType,
        Guid? pendingForEmployeeId,
        CancellationToken cancellationToken)
    {
        var query = context.Requests
            .AsNoTracking()
            .Include(r => r.Assignments)
            .Include(r => r.Decisions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(processType))
        {
            query = query.Where(r => r.ProcessType == processType);
        }

        if (pendingForEmployeeId is { } approver)
        {
            // A caixa de entrada: pedidos abertos em que esta pessoa tem uma
            // atribuição por decidir no passo em curso.
            query = query.Where(r =>
                (r.Status == ApprovalStatus.InProgress || r.Status == ApprovalStatus.ClarificationRequested)
                && r.Assignments.Any(a =>
                    a.ApproverEmployeeId == approver
                    && !a.HasDecided
                    && a.Step == r.CurrentStep));
        }

        return await query.OrderByDescending(r => r.SubmittedAt).ToListAsync(cancellationToken);
    }

    public async Task AddRequestAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        await context.Requests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
