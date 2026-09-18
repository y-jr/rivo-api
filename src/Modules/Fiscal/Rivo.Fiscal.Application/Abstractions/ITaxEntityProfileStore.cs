using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.Abstractions;

/// <summary>
/// Persistência da identidade fiscal da empresa. Definida aqui e implementada
/// em Infrastructure, para que os casos de uso não conheçam o EF Core.
///
/// <para>
/// Não tem <c>ListAsync</c>, e é deliberado: há uma identidade fiscal, não um
/// catálogo delas (ver <see cref="TaxEntityProfile"/>).
/// </para>
/// </summary>
public interface ITaxEntityProfileStore
{
    Task<TaxEntityProfile?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(TaxEntityProfile profile, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
