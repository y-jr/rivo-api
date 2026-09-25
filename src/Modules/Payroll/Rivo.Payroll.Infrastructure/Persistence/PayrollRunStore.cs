using Microsoft.EntityFrameworkCore;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Domain;
using Rivo.SharedKernel.Contracts;

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

    public async Task<(IReadOnlyList<PayrollRun> Items, int? TotalCount)> ListAsync(
        PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Runs
            .AsNoTracking()
            .Include(r => r.Items)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenBy(r => r.Id)
            .AsQueryable();

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        var itens = await query.ToListAsync(cancellationToken);
        return (itens, total);
    }

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

    /// <summary>
    /// Itens aprovados do colaborador, do mais recente para o mais antigo.
    ///
    /// <para>
    /// <strong>Ordena por <c>r.Year</c>/<c>r.Month</c> e não pela projecção.</strong>
    /// A primeira versão projectava <c>ApprovedPayrollItem</c> antes de ordenar e
    /// pedia ao SQL Server que ordenasse pelas propriedades desse registo — o EF
    /// não traduz isso, e a rota respondia 500 a qualquer colaborador que tivesse
    /// uma folha aprovada. Os testes de aplicação não o apanharam porque o duplo
    /// da persistência não é EF: só a stack real o mostra.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ApprovedPayrollItem>> ListApprovedItemsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var linhas = await context.Runs
            .AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.Approved)
            .SelectMany(
                r => r.Items.Where(i => i.EmployeeId == employeeId),
                (r, i) => new { Item = i, r.Year, r.Month })
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .ToListAsync(cancellationToken);

        return [.. linhas.Select(l => new ApprovedPayrollItem(l.Item, l.Year, l.Month))];
    }

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
