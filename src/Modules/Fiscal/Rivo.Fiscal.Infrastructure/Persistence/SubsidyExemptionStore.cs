using Microsoft.EntityFrameworkCore;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Infrastructure.Persistence;

public sealed class SubsidyExemptionStore(FiscalDbContext context) : ISubsidyExemptionStore
{
    public async Task<SubsidyExemptionSchedule?> FindAsync(
        SubsidyKind kind, CancellationToken cancellationToken) =>
        // Sem rastreio: a determinação lê e não altera. Com as versões, porque
        // é sobre elas que está a pergunta "em vigor à data".
        await context.SubsidyExemptionSchedules
            .AsNoTracking()
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Kind == kind, cancellationToken);

    public async Task<SubsidyExemptionSchedule?> FindForUpdateAsync(
        SubsidyKind kind, CancellationToken cancellationToken) =>
        // Rastreado: quem procura assim vai acrescentar uma versão, e a
        // verificação de sobreposição precisa de ver as existentes.
        await context.SubsidyExemptionSchedules
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Kind == kind, cancellationToken);

    public async Task AddAsync(SubsidyExemptionSchedule schedule, CancellationToken cancellationToken) =>
        await context.SubsidyExemptionSchedules.AddAsync(schedule, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
