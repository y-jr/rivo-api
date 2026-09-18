using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Contracts;

namespace Rivo.Fiscal.Application;

/// <summary>
/// Implementação de <see cref="ITaxEntityDirectory"/> — a porta por onde os
/// outros módulos perguntam quem emite.
///
/// <para>
/// Fina de propósito. Existe para que <c>finance</c> dependa do contrato e não
/// do caso de uso: a mesma razão pela qual <c>TaxDeterminationService</c>
/// implementa <see cref="ITaxDetermination"/> em vez de expor o seu caso de uso.
/// </para>
/// </summary>
public sealed class TaxEntityDirectory(ITaxEntityProfileStore store) : ITaxEntityDirectory
{
    public async Task<TaxEntityProfileView?> FindAsync(CancellationToken cancellationToken)
    {
        var perfil = await store.FindAsync(cancellationToken);

        return perfil is null ? null : GetTaxEntityProfile.ToView(perfil);
    }
}
