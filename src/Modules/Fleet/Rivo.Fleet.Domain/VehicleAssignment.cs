namespace Rivo.Fleet.Domain;

/// <summary>
/// Atribuição de uma viatura a um motorista — parte do agregado
/// <see cref="Vehicle"/> (`modules/fleet.md` §Possui).
///
/// <para>
/// Nasce sempre por <see cref="Vehicle.Assign"/>: não tem vida fora da
/// viatura a que pertence, por isso o construtor é <c>internal</c>. Uma
/// viatura só tem uma atribuição aberta de cada vez — não há partilha
/// concorrente entre dois motoristas neste modelo.
/// </para>
///
/// <para>
/// <strong>Referencia o Colaborador só por identificador</strong> (ADR-010)
/// — `fleet` não possui informação de colaborador (`modules/fleet.md`
/// §Não pode); lê-a pelo contrato de `hr` quando precisar de a mostrar, e
/// nunca a copia (BR-18).
/// </para>
/// </summary>
public sealed class VehicleAssignment
{
    internal VehicleAssignment(Guid id, Guid vehicleId, Guid employeeId, DateOnly startedOn)
    {
        Id = id;
        VehicleId = vehicleId;
        EmployeeId = employeeId;
        StartedOn = startedOn;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private VehicleAssignment()
    {
    }

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly? EndedOn { get; private set; }

    /// <summary>Verdadeiro enquanto a atribuição ainda não terminou.</summary>
    public bool IsOpen => EndedOn is null;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal void End(DateOnly endedOn)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Esta atribuição já terminou.");
        }

        if (endedOn < StartedOn)
        {
            throw new ArgumentException(
                "A data de fim não pode ser anterior ao início da atribuição.", nameof(endedOn));
        }

        EndedOn = endedOn;
    }
}
