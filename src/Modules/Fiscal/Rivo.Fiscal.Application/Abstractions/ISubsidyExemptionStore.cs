using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.Abstractions;

/// <summary>
/// Persistência dos limiares de isenção de subsídios. Definida aqui e
/// implementada em Infrastructure, para que os casos de uso não conheçam o
/// EF Core.
///
/// <para>
/// <strong>Uma série por <see cref="SubsidyKind"/></strong> — ao contrário
/// de <see cref="ITaxRateStore"/> (imposto + código), aqui o tipo de
/// subsídio já identifica a série sozinho, por isso os métodos levam
/// <see cref="SubsidyKind"/> em vez de par.
/// </para>
/// </summary>
public interface ISubsidyExemptionStore
{
    /// <summary>Sem rastreio, com as versões: a determinação lê e não altera.</summary>
    Task<SubsidyExemptionSchedule?> FindAsync(SubsidyKind kind, CancellationToken cancellationToken);

    /// <summary>Rastreado, com as versões: quem procura assim vai acrescentar uma.</summary>
    Task<SubsidyExemptionSchedule?> FindForUpdateAsync(SubsidyKind kind, CancellationToken cancellationToken);

    Task AddAsync(SubsidyExemptionSchedule schedule, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
