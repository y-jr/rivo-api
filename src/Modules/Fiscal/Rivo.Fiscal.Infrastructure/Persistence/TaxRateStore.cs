using Microsoft.EntityFrameworkCore;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Fiscal.Infrastructure.Persistence;

public sealed class TaxRateStore(FiscalDbContext context) : ITaxRateStore
{
    public async Task<TaxRateSchedule?> FindAsync(
        TaxKind kind,
        string code,
        CancellationToken cancellationToken) =>
        // Sem rastreio: a determinação lê e não altera. Com as versões, porque
        // é sobre elas que está a pergunta "em vigor à data".
        await context.Schedules
            .AsNoTracking()
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Kind == kind && s.Code == code, cancellationToken);

    public async Task<TaxRateSchedule?> FindByIdAsync(
        Guid scheduleId,
        CancellationToken cancellationToken) =>
        // Rastreado: quem procura por identificador vai acrescentar uma versão,
        // e a verificação de sobreposição precisa de ver as existentes.
        await context.Schedules
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);

    public async Task<(IReadOnlyList<TaxRateSchedule> Items, int? TotalCount)> ListAsync(
        PageRequest? pagina, CancellationToken cancellationToken)
    {
        var query = context.Schedules
            .AsNoTracking()
            .Include(s => s.Versions)
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Code)
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

    public async Task AddAsync(TaxRateSchedule schedule, CancellationToken cancellationToken) =>
        await context.Schedules.AddAsync(schedule, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
