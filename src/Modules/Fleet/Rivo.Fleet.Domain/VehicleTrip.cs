namespace Rivo.Fleet.Domain;

/// <summary>
/// Registo de viagem de uma viatura — parte do agregado <see cref="Vehicle"/>
/// (`modules/fleet.md` §Possui). Controlo de quilometragem: o quanto se
/// percorreu, não um itinerário detalhado.
///
/// <para>
/// Nasce sempre por <see cref="Vehicle.RegisterTrip"/> — nunca directamente,
/// por isso o construtor é <c>internal</c>. **Nunca se altera nem se elimina
/// depois de criado** (BR-9, BR-14): é o registo do que aconteceu, mesma
/// disciplina de <c>StockMovement</c> em `inventory`. Sem estado
/// aberto/fechado de propósito — ao contrário de Manutenção e Atribuição, uma
/// viagem regista-se como facto já concluído, não como algo que se abre agora
/// e se fecha depois.
/// </para>
///
/// <para>
/// <strong>O motorista é opcional</strong>, ao contrário de
/// <see cref="VehicleAssignment"/> — uma viatura pode ser usada sem
/// atribuição formal (uso pontual), e o registo de viagem existe para captar
/// isso também. Quando indicado, referencia o Colaborador só por
/// identificador (ADR-010); a Application verifica que existe em `hr` antes
/// de gravar, e nunca copia nome nem cargo (BR-18).
/// </para>
/// </summary>
public sealed class VehicleTrip
{
    internal VehicleTrip(
        Guid id,
        Guid vehicleId,
        Guid? driverId,
        DateOnly startedOn,
        DateOnly endedOn,
        decimal startOdometer,
        decimal endOdometer,
        string? purpose)
    {
        Id = id;
        VehicleId = vehicleId;
        DriverId = driverId;
        StartedOn = startedOn;
        EndedOn = endedOn;
        StartOdometer = startOdometer;
        EndOdometer = endOdometer;
        Purpose = purpose;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private VehicleTrip()
    {
    }

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid? DriverId { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly EndedOn { get; private set; }

    public decimal StartOdometer { get; private set; }

    public decimal EndOdometer { get; private set; }

    /// <summary>Motivo ou destino da viagem — livre, opcional.</summary>
    public string? Purpose { get; private set; }

    /// <summary>Quilómetros percorridos — a diferença entre os dois odómetros, nunca escrita directamente.</summary>
    public decimal Distance => EndOdometer - StartOdometer;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }
}
