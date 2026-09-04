namespace Rivo.Fleet.Domain;

/// <summary>
/// Viatura — agregado raiz de `fleet` (ver `modules/fleet.md`).
///
/// <para>
/// <strong>Manutenção, Atribuição, Plano de Manutenção, Registo de Viagem e
/// Despesa de Frota vivem aqui dentro</strong> (§Possui): nascem sempre por
/// este agregado (<see cref="OpenMaintenance"/>, <see cref="Assign"/>,
/// <see cref="SchedulePlan"/>, <see cref="RegisterTrip"/>,
/// <see cref="RegisterExpense"/>). Seguros e documentação legal vivem à
/// parte, em <see cref="VehicleDocument"/> — não têm invariante que dependa
/// dos outros filhos, por isso não precisam do limite de consistência do
/// agregado (mesma razão de <c>EmployeeDocument</c> em `hr`).
/// </para>
/// </summary>
public sealed class Vehicle
{
    private readonly List<MaintenanceRecord> _maintenances = [];
    private readonly List<VehicleAssignment> _assignments = [];
    private readonly List<MaintenancePlan> _plans = [];
    private readonly List<VehicleTrip> _trips = [];
    private readonly List<FleetExpense> _expenses = [];

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

    public IReadOnlyList<MaintenancePlan> Plans => _plans;

    public IReadOnlyList<VehicleTrip> Trips => _trips;

    public IReadOnlyList<FleetExpense> Expenses => _expenses;

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

    /// <summary>
    /// Fecha o registo de manutenção aberto e devolve a viatura a activa.
    /// <paramref name="cost"/> é opcional (ADR-048) — nem toda a manutenção
    /// tem custo a registar.
    /// </summary>
    public void CloseMaintenance(Guid maintenanceId, DateOnly endedOn, decimal? cost = null)
    {
        var registo = _maintenances.FirstOrDefault(m => m.Id == maintenanceId)
            ?? throw new InvalidOperationException("Registo de manutenção não encontrado nesta viatura.");

        registo.Close(endedOn, cost);
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

    /// <summary>
    /// Agenda um plano de manutenção preventiva. Vários planos activos ao
    /// mesmo tempo são normais — ver <see cref="MaintenancePlan"/>.
    /// </summary>
    public MaintenancePlan SchedulePlan(string description, int intervalDays, DateOnly firstDueOn)
    {
        EnsureNotInactive("agendar um plano de manutenção");

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Um plano de manutenção precisa de descrição.", nameof(description));
        }

        if (intervalDays <= 0)
        {
            throw new ArgumentException("O intervalo tem de ser positivo.", nameof(intervalDays));
        }

        var plano = new MaintenancePlan(Guid.CreateVersion7(), Id, description.Trim(), intervalDays, firstDueOn);
        _plans.Add(plano);

        return plano;
    }

    /// <summary>Regista que o ciclo actual do plano foi concluído e reagenda o próximo.</summary>
    public void CompletePlanCycle(Guid planId, DateOnly completedOn)
    {
        EnsureNotInactive("concluir um ciclo de manutenção");
        FindPlan(planId).CompleteCycle(completedOn);
    }

    /// <summary>
    /// Cancela um plano. Sem guarda de <see cref="Status"/> de propósito —
    /// cancelar os planos de uma viatura que acabou de ficar inactiva é o
    /// que se espera, não algo a bloquear.
    /// </summary>
    public void CancelPlan(Guid planId) => FindPlan(planId).Cancel();

    private MaintenancePlan FindPlan(Guid planId) =>
        _plans.FirstOrDefault(p => p.Id == planId)
            ?? throw new InvalidOperationException("Plano de manutenção não encontrado nesta viatura.");

    /// <summary>
    /// Regista uma viagem já concluída — controlo de quilometragem, não um
    /// itinerário. Ao contrário de Manutenção e Atribuição, não há
    /// abrir/fechar: a viagem entra já com início e fim.
    ///
    /// <para>
    /// <paramref name="driverId"/> é opcional; quando indicado, quem verifica
    /// que é um Colaborador que existe é a Application, contra o contrato de
    /// `hr` (ADR-010) — o agregado só sabe que é um identificador.
    /// </para>
    /// </summary>
    public VehicleTrip RegisterTrip(
        Guid? driverId, DateOnly startedOn, DateOnly endedOn, decimal startOdometer, decimal endOdometer, string? purpose)
    {
        EnsureNotInactive("registar uma viagem");

        if (driverId is { } id && id == Guid.Empty)
        {
            throw new ArgumentException("O motorista, quando indicado, tem de ser um identificador válido.", nameof(driverId));
        }

        if (endedOn < startedOn)
        {
            throw new ArgumentException("A data de fim não pode ser anterior ao início da viagem.", nameof(endedOn));
        }

        if (startOdometer < 0)
        {
            throw new ArgumentException("O odómetro inicial não pode ser negativo.", nameof(startOdometer));
        }

        if (endOdometer < startOdometer)
        {
            throw new ArgumentException(
                "O odómetro final não pode ser anterior ao inicial.", nameof(endOdometer));
        }

        var viagem = new VehicleTrip(
            Guid.CreateVersion7(), Id, driverId, startedOn, endedOn, startOdometer, endOdometer, purpose?.Trim());
        _trips.Add(viagem);

        return viagem;
    }

    /// <summary>
    /// Regista uma despesa de frota — combustível, portagem ou
    /// estacionamento. Facto operacional, sem postagem automática no razão
    /// (`modules/fleet.md` §Não pode) — ver a nota em
    /// <see cref="FleetExpense"/>.
    /// </summary>
    public FleetExpense RegisterExpense(
        FleetExpenseCategory category, decimal amount, DateOnly occurredOn, string? description)
    {
        EnsureNotInactive("registar uma despesa");

        if (amount <= 0)
        {
            throw new ArgumentException("A despesa tem de ter um valor positivo.", nameof(amount));
        }

        var despesa = new FleetExpense(Guid.CreateVersion7(), Id, category, amount, occurredOn, description?.Trim());
        _expenses.Add(despesa);

        return despesa;
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
