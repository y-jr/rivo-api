namespace Rivo.Fleet.Domain;

/// <summary>
/// Viatura — agregado raiz de `fleet` (ver `modules/fleet.md`).
///
/// <para>
/// <strong>Manutenção e Atribuição vivem aqui dentro</strong> (§Possui):
/// nascem sempre por este agregado (<see cref="OpenMaintenance"/>,
/// <see cref="Assign"/>). Plano de Manutenção (calendário preventivo com
/// alertas), Registo de Viagem, Despesa de Frota e Seguros continuam por
/// fazer.
/// </para>
/// </summary>
public sealed class Vehicle
{
    private readonly List<MaintenanceRecord> _maintenances = [];
    private readonly List<VehicleAssignment> _assignments = [];

    private Vehicle(Guid id, string plateNumber, string model)
    {
        Id = id;
        PlateNumber = plateNumber;
        Model = model;
        Status = VehicleStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Vehicle()
    {
        PlateNumber = string.Empty;
        Model = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Matrícula, normalizada em maiúsculas.</summary>
    public string PlateNumber { get; private set; }

    public string Model { get; private set; }

    public VehicleStatus Status { get; private set; }

    public IReadOnlyList<MaintenanceRecord> Maintenances => _maintenances;

    public IReadOnlyList<VehicleAssignment> Assignments => _assignments;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Vehicle Register(string plateNumber, string model)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            throw new ArgumentException("Uma viatura precisa de matrícula.", nameof(plateNumber));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Uma viatura precisa de modelo.", nameof(model));
        }

        return new Vehicle(
            Guid.CreateVersion7(), plateNumber.Trim().ToUpperInvariant(), model.Trim());
    }

    /// <summary>
    /// Abre um registo de manutenção. Só um de cada vez — enquanto um estiver
    /// aberto, <see cref="Status"/> já diz <c>InMaintenance</c>, e é essa a
    /// exclusividade que aqui se impõe.
    /// </summary>
    public MaintenanceRecord OpenMaintenance(MaintenanceType type, string description, DateOnly startedOn)
    {
        EnsureNotInactive("enviar para manutenção");

        if (Status is VehicleStatus.InMaintenance)
        {
            throw new InvalidOperationException("Esta viatura já está em manutenção.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Uma manutenção precisa de descrição.", nameof(description));
        }

        var registo = new MaintenanceRecord(Guid.CreateVersion7(), Id, type, description.Trim(), startedOn);
        _maintenances.Add(registo);
        Status = VehicleStatus.InMaintenance;

        return registo;
    }

    /// <summary>Fecha o registo de manutenção aberto e devolve a viatura a activa.</summary>
    public void CloseMaintenance(Guid maintenanceId, DateOnly endedOn)
    {
        var registo = _maintenances.FirstOrDefault(m => m.Id == maintenanceId)
            ?? throw new InvalidOperationException("Registo de manutenção não encontrado nesta viatura.");

        registo.Close(endedOn);
        Status = VehicleStatus.Active;
    }

    /// <summary>
    /// Atribui a viatura a um motorista. Só uma atribuição aberta de cada
    /// vez — reatribuir exige terminar a actual primeiro
    /// (<see cref="EndAssignment"/>), nunca a substitui em silêncio.
    ///
    /// <para>
    /// Quem verifica que <paramref name="employeeId"/> é um Colaborador que
    /// existe é a camada Application, contra o contrato de `hr` (ADR-010) —
    /// o agregado só sabe que é um identificador.
    /// </para>
    /// </summary>
    public VehicleAssignment Assign(Guid employeeId, DateOnly startedOn)
    {
        EnsureNotInactive("atribuir");

        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("Uma atribuição precisa de motorista.", nameof(employeeId));
        }

        if (_assignments.Any(a => a.IsOpen))
        {
            throw new InvalidOperationException(
                "Esta viatura já está atribuída — termine a atribuição actual antes de atribuir de novo.");
        }

        var atribuicao = new VehicleAssignment(Guid.CreateVersion7(), Id, employeeId, startedOn);
        _assignments.Add(atribuicao);

        return atribuicao;
    }

    public void EndAssignment(Guid assignmentId, DateOnly endedOn)
    {
        var atribuicao = _assignments.FirstOrDefault(a => a.Id == assignmentId)
            ?? throw new InvalidOperationException("Atribuição não encontrada nesta viatura.");

        atribuicao.End(endedOn);
    }

    public void Deactivate()
    {
        Status = VehicleStatus.Inactive;
    }

    private void EnsureNotInactive(string acto)
    {
        if (Status is VehicleStatus.Inactive)
        {
            throw new InvalidOperationException($"Não é possível {acto}: a viatura está inactiva.");
        }
    }
}

public enum VehicleStatus
{
    Active,
    InMaintenance,
    Inactive,
}
