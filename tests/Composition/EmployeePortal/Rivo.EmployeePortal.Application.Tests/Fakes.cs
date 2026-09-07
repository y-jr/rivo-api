using Rivo.Hr.Contracts;

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
/// Duplo do contrato de auto-serviço de `hr`.
///
/// <para>
/// <strong>Regista o <c>employeeId</c> que recebeu</strong>, e é isso que
/// torna os testes úteis: o que se quer provar não é que devolve a lista, é
/// que o portal pediu a lista <em>da pessoa certa</em>. Um duplo que só
/// devolvesse dados deixaria passar um portal que perguntasse pelo colaborador
/// errado.
/// </para>
/// </summary>
internal sealed class FakeEmployeeSelfService : IEmployeeSelfService
{
    public Guid? EmployeeIdPedido { get; private set; }

    public DateOnly? DeQuePediu { get; private set; }

    public DateOnly? AtePediu { get; private set; }

    public IReadOnlyList<OwnAttendanceRecord> Attendance { get; init; } = [];

    public IReadOnlyList<OwnLeaveRequest> Leave { get; init; } = [];

    public IReadOnlyList<OwnDocument> Documents { get; init; } = [];

    public Task<IReadOnlyList<OwnAttendanceRecord>> ListAttendanceAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        EmployeeIdPedido = employeeId;
        DeQuePediu = from;
        AtePediu = to;
        return Task.FromResult(Attendance);
    }

    public Task<IReadOnlyList<OwnLeaveRequest>> ListLeaveAsync(
        Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeIdPedido = employeeId;
        return Task.FromResult(Leave);
    }

    public Task<IReadOnlyList<OwnDocument>> ListDocumentsAsync(
        Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeIdPedido = employeeId;
        return Task.FromResult(Documents);
    }
}

/// <summary>Duplo do contrato de recibos de `payroll`.</summary>
internal sealed class FakePayrollSelfService : Rivo.Payroll.Contracts.IPayrollSelfService
{
    public Guid? EmployeeIdPedido { get; private set; }

    public IReadOnlyList<Rivo.Payroll.Contracts.OwnPayslip> Payslips { get; init; } = [];

    public Task<IReadOnlyList<Rivo.Payroll.Contracts.OwnPayslip>> ListPayslipsAsync(
        Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeIdPedido = employeeId;
        return Task.FromResult(Payslips);
    }
}
