using Microsoft.EntityFrameworkCore;
using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Domain;
using Rivo.SharedKernel.Contracts;

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

    public async Task<(IReadOnlyList<ApprovalRequest> Items, int? TotalCount)> ListRequestsAsync(
        string? processType,
        Guid? pendingForEmployeeId,
        PageRequest? pagina,
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
            //
            // Exclui quem submeteu o próprio pedido, mesmo que apareça
            // atribuído ao passo em curso — BR-2 recusa-lhe sempre a decisão
            // (ApprovalRequest.Decide, "nem sequer alguém atribuído por
            // engano ao próprio pedido a contorna"), e mostrar aqui um
            // pedido que a pessoa nunca vai conseguir decidir é a caixa de
            // entrada a mentir sobre o que está mesmo pendente para ela.
            query = query.Where(r =>
                (r.Status == ApprovalStatus.InProgress || r.Status == ApprovalStatus.ClarificationRequested)
                && r.RequestedByEmployeeId != approver
                && r.Assignments.Any(a =>
                    a.ApproverEmployeeId == approver
                    && !a.HasDecided
                    && a.Step == r.CurrentStep));
        }

        query = query.OrderByDescending(r => r.SubmittedAt).ThenBy(r => r.Id);

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        var itens = await query.ToListAsync(cancellationToken);
        return (itens, total);
    }

    public async Task AddRequestAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        await context.Requests.AddAsync(request, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
