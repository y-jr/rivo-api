using Microsoft.EntityFrameworkCore;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class FiscalDocumentFileStore(FinanceDbContext context) : IFiscalDocumentFileStore
{
    /// <summary>
    /// Sem rastreio: estas linhas nunca se alteram depois de escritas, só se
    /// acrescentam.
    /// </summary>
    public async Task<FiscalDocumentFile?> FindAsync(
        FiscalDocumentKind kind,
        Guid sourceDocumentId,
        bool reflectsCancellation,
        CancellationToken cancellationToken) =>
        await context.FiscalDocumentFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                f => f.Kind == kind
                    && f.SourceDocumentId == sourceDocumentId
                    && f.ReflectsCancellation == reflectsCancellation,
                cancellationToken);

    public async Task AddAsync(FiscalDocumentFile file, CancellationToken cancellationToken) =>
        await context.FiscalDocumentFiles.AddAsync(file, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
