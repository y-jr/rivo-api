using Microsoft.EntityFrameworkCore;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Infrastructure.Persistence;

public sealed class PayrollRunStore(PayrollDbContext context) : IPayrollRunStore
{
    public async Task<PayrollRun?> FindAsync(Guid runId, CancellationToken cancellationToken) =>
        await context.Runs
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task<PayrollRun?> FindForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
        await context.Runs
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task<IReadOnlyList<PayrollRun>> ListAsync(CancellationToken cancellationToken) =>
        await context.Runs
            .AsNoTracking()
            .Include(r => r.Items)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PayrollRun run, CancellationToken cancellationToken) =>
        await context.Runs.AddAsync(run, cancellationToken);

    public async Task AddPayrollItemDocumentAsync(
        PayrollItemDocument link, CancellationToken cancellationToken) =>
        await context.ItemDocuments.AddAsync(link, cancellationToken);

    public async Task<IReadOnlyList<PayrollItemDocument>> ListPayrollItemDocumentsAsync(
        Guid payrollItemId, CancellationToken cancellationToken) =>
        await context.ItemDocuments
            .AsNoTracking()
            .Where(link => link.PayrollItemId == payrollItemId)
            .OrderByDescending(link => link.AttachedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovedPayrollItem>> ListApprovedItemsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        await context.Runs
            .AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.Approved)
            .SelectMany(
                r => r.Items.Where(i => i.EmployeeId == employeeId),
                (r, i) => new ApprovedPayrollItem(i, r.Year, r.Month))
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollItemDocument>> ListDocumentsForItemsAsync(
        IReadOnlyList<Guid> payrollItemIds,
        CancellationToken cancellationToken) =>
        payrollItemIds.Count == 0
            ? []
            : await context.ItemDocuments
                .AsNoTracking()
                .Where(link => payrollItemIds.Contains(link.PayrollItemId))
                .OrderByDescending(link => link.AttachedAt)
                .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
