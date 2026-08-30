using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.Abstractions;

/// <summary>
/// Persistência da tabela de escalões de IRT. Definida aqui e implementada
/// em Infrastructure, para que os casos de uso não conheçam o EF Core.
///
/// <para>
/// <strong>Singleton de facto</strong> — só há uma <see cref="IncomeTaxSchedule"/>,
/// por isso não há método por identificador: <see cref="FindAsync"/> e
/// <see cref="FindForUpdateAsync"/> não levam parâmetro.
/// </para>
/// </summary>
public interface IIncomeTaxScheduleStore
{
    /// <summary>Sem rastreio, com as versões: a determinação lê e não altera.</summary>
    Task<IncomeTaxSchedule?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Rastreado, com as versões: quem procura assim vai acrescentar uma.</summary>
    Task<IncomeTaxSchedule?> FindForUpdateAsync(CancellationToken cancellationToken);

    Task AddAsync(IncomeTaxSchedule schedule, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
