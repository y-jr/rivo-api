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

    /// <summary>
    /// Recibos aprovados de um colaborador, mais o documento de cada item.
    ///
    /// <para>
    /// <strong>Duas consultas e não um `join`.</strong> Os itens e os
    /// documentos vivem em tabelas diferentes e nem todos os itens têm
    /// documento; um `left join` traria a mesma folha repetida por cada item e
    /// obrigaria a desdobrar do lado do cliente. Duas consultas pequenas com o
    /// filtro já aplicado são menos linhas na rede e mais fáceis de ler.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<(PayrollRun Run, PayrollItem Item, Guid? DocumentId)>>
        ListApprovedPayslipsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var folhas = await context.Runs
            .AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.Status == PayrollRunStatus.Approved
                && r.Items.Any(i => i.EmployeeId == employeeId))
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .ToListAsync(cancellationToken);

        if (folhas.Count == 0)
        {
            return [];
        }

        var itens = folhas
            .Select(r => (Run: r, Item: r.Items.First(i => i.EmployeeId == employeeId)))
            .ToList();

        var ids = itens.Select(p => p.Item.Id).ToList();

        var documentos = await context.ItemDocuments
            .AsNoTracking()
            .Where(d => ids.Contains(d.PayrollItemId))
            .ToListAsync(cancellationToken);

        var porItem = documentos
            .GroupBy(d => d.PayrollItemId)
            .ToDictionary(g => g.Key, g => g.First().DocumentId);

        return
        [
            .. itens.Select(p => (p.Run, p.Item, (Guid?)porItem.GetValueOrDefault(p.Item.Id))),
        ];
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

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
