using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;
using Rivo.Hr.Contracts;

namespace Rivo.Fleet.Application.Tests;

/// <summary>
/// Viaturas em memória.
///
/// <para>
/// <c>ListWithDuePlansAsync</c> devolve <strong>tudo</strong>, de propósito.
/// O caso de uso volta a filtrar em memória, e é esse filtro que se quer
/// exercitar — um caso de uso que confiasse no armazenamento ter filtrado
/// passaria neste teste com um filtro próprio errado, e é precisamente isso
/// que não pode acontecer.
/// </para>
/// </summary>
internal sealed class FakeVehicleStore : IVehicleStore
{
    private readonly List<Vehicle> _viaturas = [];

    public int Gravacoes { get; private set; }

    public Vehicle Registar(string matricula, bool activa = true)
    {
        var viatura = Vehicle.Register(matricula, "Modelo");
        if (!activa) viatura.Deactivate();
        _viaturas.Add(viatura);
        return viatura;
    }

    public Task<Vehicle?> FindAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        Task.FromResult(_viaturas.SingleOrDefault(v => v.Id == vehicleId));

    public Task<Vehicle?> FindForUpdateAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        Task.FromResult(_viaturas.SingleOrDefault(v => v.Id == vehicleId));

    public Task<Vehicle?> FindByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_viaturas.SingleOrDefault(v => v.PlateNumber == plateNumber));

    public Task<IReadOnlyList<Vehicle>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Vehicle>>([.. _viaturas]);

    /// <summary>Sem filtrar — ver a nota da classe.</summary>
    public Task<IReadOnlyList<Vehicle>> ListWithDuePlansAsync(
        DateOnly asOf, int withinDays, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Vehicle>>([.. _viaturas]);

    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        _viaturas.Add(vehicle);
        return Task.CompletedTask;
    }

    public Task<decimal> SumExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a SumExpensesAsync.");

    public Task<decimal> SumTripDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a SumTripDistanceAsync.");

    public Task<decimal> SumMaintenanceCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a SumMaintenanceCostAsync.");

    public Task AddVehicleDocumentAsync(VehicleDocument link, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a AddVehicleDocumentAsync.");

    public Task<IReadOnlyList<VehicleDocument>> ListVehicleDocumentsAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a ListVehicleDocumentsAsync.");

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>O colaborador vem de `hr` pelo contrato (ADR-010).</summary>
internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly HashSet<Guid> _existentes = [];

    public Guid Existente()
    {
        var id = Guid.NewGuid();
        _existentes.Add(id);
        return id;
    }

    public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult(_existentes.Contains(employeeId)
            ? new EmployeeReference(employeeId, "Condutor", EmployeeStatus.Active, null, null, null)
            : null);

    public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a FindByUserIdAsync.");

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a FindByPositionAsync.");

    public Task<EmployeeHireResult> HireAsync(
        string fullName, string? departmentName, DateTimeOffset hiredOn, Guid actorId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu uma chamada a HireAsync.");
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
