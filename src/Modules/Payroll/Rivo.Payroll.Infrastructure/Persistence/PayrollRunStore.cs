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

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
