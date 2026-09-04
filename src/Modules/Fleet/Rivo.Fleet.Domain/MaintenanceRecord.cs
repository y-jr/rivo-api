namespace Rivo.Fleet.Domain;

/// <summary>
/// Registo de manutenção de uma viatura — parte do agregado
/// <see cref="Vehicle"/> (`modules/fleet.md` §Possui).
///
/// <para>
/// Nasce sempre por <see cref="Vehicle.OpenMaintenance"/>: não tem vida fora
/// da viatura a que pertence, por isso o construtor é <c>internal</c>. Uma
/// viatura só tem um registo aberto de cada vez — é o que dá sentido a
/// <see cref="Vehicle.Status"/> continuar a existir como resumo de estado.
/// </para>
///
/// <para>
/// <strong>Não é o Plano de Manutenção</strong> (`modules/fleet.md` §Possui)
/// — este é o registo do que aconteceu, não o calendário preventivo com
/// alertas. Esse continua por fazer.
/// </para>
/// </summary>
public sealed class MaintenanceRecord
{
    internal MaintenanceRecord(
        Guid id, Guid vehicleId, MaintenanceType type, string description, DateOnly startedOn)
    {
        Id = id;
        VehicleId = vehicleId;
        Type = type;
        Description = description;
        StartedOn = startedOn;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private MaintenanceRecord()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public MaintenanceType Type { get; private set; }

    public string Description { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly? EndedOn { get; private set; }

    /// <summary>
    /// Custo da intervenção — opcional (2026-09-04, ADR-048). Nulo em todo o
    /// histórico anterior a esta data, porque o campo não existia; nulo
    /// também daqui em diante para quem não regista custo por manutenção
    /// (ex.: trabalho interno, garantia). Preenchido ao fechar
    /// (<see cref="Close"/>), nunca à abertura — é quando se sabe o valor
    /// final, não uma estimativa.
    /// </summary>
    public decimal? Cost { get; private set; }

    /// <summary>Verdadeiro enquanto a manutenção ainda não fechou.</summary>
    public bool IsOpen => EndedOn is null;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal void Close(DateOnly endedOn, decimal? cost = null)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Este registo de manutenção já está fechado.");
        }

        if (endedOn < StartedOn)
        {
            throw new ArgumentException(
                "A data de fecho não pode ser anterior ao início da manutenção.", nameof(endedOn));
        }

        if (cost is < 0)
        {
            throw new ArgumentException("O custo da manutenção não pode ser negativo.", nameof(cost));
        }

        EndedOn = endedOn;
        Cost = cost;
    }
}

public enum MaintenanceType
{
    Preventive,
    Corrective,
}
