using Rivo.Hr.Contracts;
using Rivo.Payroll.Contracts;

namespace Rivo.EmployeePortal.Application.Tests;

/// <summary>Duplo escrito à mão, sem biblioteca de mocks — ADR-022.</summary>
internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly Dictionary<Guid, EmployeeReference> _byUserId = [];

    public FakeEmployeeDirectory WithEmployee(Guid userId, EmployeeReference employee)
    {
        _byUserId[userId] = employee;
        return this;
    }

    public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.Values.FirstOrDefault(e => e.EmployeeId == employeeId));

    public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.GetValueOrDefault(userId));

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(
        Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeReference>>([]);

    public Task<EmployeeHireResult> HireAsync(
        string fullName, string? departmentName, DateTimeOffset hiredOn, Guid actorId, CancellationToken cancellationToken) =>
        Task.FromResult(EmployeeHireResult.Success(Guid.CreateVersion7()));
}

/// <summary>
/// Registra o <c>employeeId</c> com que foi chamado. É essa a asserção que
/// interessa nestes testes: que chega o identificador do colaborador ligado, e
/// não o da conta.
/// </summary>
internal sealed class FakeEmployeeSelfService : IEmployeeSelfService
{
    public List<Guid> AttendanceCalls { get; } = [];

    public List<Guid> LeaveCalls { get; } = [];

    public List<Guid> DocumentCalls { get; } = [];

    public (DateOnly From, DateOnly To)? UltimaJanela { get; private set; }

    public Task<IReadOnlyList<OwnAttendanceRecord>> ListAttendanceAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        AttendanceCalls.Add(employeeId);
        UltimaJanela = (from, to);

        return Task.FromResult<IReadOnlyList<OwnAttendanceRecord>>(
        [
            new OwnAttendanceRecord(Guid.NewGuid(), from, null, null, "Present", null, 8),
        ]);
    }

    public Task<IReadOnlyList<OwnLeaveRequest>> ListLeaveAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        LeaveCalls.Add(employeeId);

        return Task.FromResult<IReadOnlyList<OwnLeaveRequest>>([]);
    }

    public Task<IReadOnlyList<OwnEmployeeDocument>> ListDocumentsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        DocumentCalls.Add(employeeId);

        return Task.FromResult<IReadOnlyList<OwnEmployeeDocument>>([]);
    }
}

internal sealed class FakePayrollSelfService : IPayrollSelfService
{
    public List<Guid> PayslipCalls { get; } = [];

    public Task<IReadOnlyList<OwnPayslip>> ListPayslipsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        PayslipCalls.Add(employeeId);

        return Task.FromResult<IReadOnlyList<OwnPayslip>>([]);
    }
}
