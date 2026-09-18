using Microsoft.EntityFrameworkCore;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Infrastructure.Persistence;

public sealed class TaxEntityProfileStore(FiscalDbContext context) : ITaxEntityProfileStore
{
    /// <summary>
    /// Rastreado, e é decisão.
    ///
    /// <para>
    /// A leitura e a correcção usam o mesmo método porque a operação de escrita
    /// é "declarar ou corrigir" (ver <c>DeclareTaxEntityProfile</c>): precisa de
    /// descobrir se já existe e, se existir, de alterar a mesma instância. Um
    /// <c>AsNoTracking</c> aqui fazia a correcção gravar silenciosamente nada.
    /// </para>
    ///
    /// <para>
    /// A leitura de cabeçalho é pequena e cabe uma vez por documento gerado, por
    /// isso o custo do rastreio não paga uma segunda via só de leitura.
    /// </para>
    /// </summary>
    public async Task<TaxEntityProfile?> FindAsync(CancellationToken cancellationToken) =>
        await context.TaxEntityProfiles
            .FirstOrDefaultAsync(p => p.Id == TaxEntityProfile.WellKnownId, cancellationToken);

    public async Task AddAsync(TaxEntityProfile profile, CancellationToken cancellationToken) =>
        await context.TaxEntityProfiles.AddAsync(profile, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
