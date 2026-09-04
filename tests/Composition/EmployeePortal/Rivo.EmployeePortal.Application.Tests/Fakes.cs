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
