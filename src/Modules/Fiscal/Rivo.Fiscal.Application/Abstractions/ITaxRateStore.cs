using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.Abstractions;

/// <summary>
/// Persistência de `fiscal`. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
/// </summary>
public interface ITaxRateStore
{
    /// <summary>
    /// A série de um imposto e código, <strong>com as versões</strong>.
    ///
    /// <para>
    /// As versões vêm carregadas porque é sobre elas que estão as duas
    /// invariantes que importam — não sobreposição e "em vigor à data". Trazer
    /// a série sem elas faria a determinação responder "sem taxa em vigor" com
    /// a tabela cheia.
    /// </para>
    /// </summary>
    Task<TaxRateSchedule?> FindAsync(TaxKind kind, string code, CancellationToken cancellationToken);

    Task<TaxRateSchedule?> FindByIdAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaxRateSchedule>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(TaxRateSchedule schedule, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
