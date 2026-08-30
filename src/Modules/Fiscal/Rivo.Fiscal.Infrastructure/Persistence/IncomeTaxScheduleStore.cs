using Microsoft.EntityFrameworkCore;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Infrastructure.Persistence;

public sealed class IncomeTaxScheduleStore(FiscalDbContext context) : IIncomeTaxScheduleStore
{
    public async Task<IncomeTaxSchedule?> FindAsync(CancellationToken cancellationToken) =>
        // Sem rastreio: a determinação lê e não altera. Com as versões, porque
        // é sobre elas que está a pergunta "em vigor à data".
        await context.IncomeTaxSchedules
            .AsNoTracking()
            .Include(s => s.Versions)
            .ThenInclude(v => v.Brackets)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IncomeTaxSchedule?> FindForUpdateAsync(CancellationToken cancellationToken) =>
        // Rastreado: quem procura assim vai acrescentar uma versão, e a
        // verificação de sobreposição precisa de ver as existentes.
        await context.IncomeTaxSchedules
            .Include(s => s.Versions)
            .ThenInclude(v => v.Brackets)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(IncomeTaxSchedule schedule, CancellationToken cancellationToken) =>
        await context.IncomeTaxSchedules.AddAsync(schedule, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
