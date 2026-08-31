using Rivo.Hr.Contracts;

namespace Rivo.EmployeePortal.Application.Tests;

public class GetMyProfileTests
{
    [Fact]
    public async Task ExecuteAsync_UserLinkedToEmployee_ReturnsFound()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory().WithEmployee(
            userId,
            new EmployeeReference(
                employeeId,
                "Ana Silva",
                EmployeeStatus.Active,
                departmentId,
                new PositionReference(Guid.NewGuid(), "Contabilista", GrantsApprovalAuthority: false),
                userId));
        var useCase = new GetMyProfile(directory);

        var result = await useCase.ExecuteAsync(userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyProfileOutcome.Found, result.Outcome);
        Assert.NotNull(result.Profile);
        Assert.Equal(employeeId, result.Profile!.EmployeeId);
        Assert.Equal("Ana Silva", result.Profile.DisplayName);
        Assert.Equal(departmentId, result.Profile.DepartmentId);
        Assert.Equal("Contabilista", result.Profile.CurrentPosition!.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutEmployeeLink_ReturnsNotLinked()
    {
        var useCase = new GetMyProfile(new FakeEmployeeDirectory());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyProfileOutcome.NotLinked, result.Outcome);
        Assert.Null(result.Profile);
    }

    [Fact]
    public async Task ExecuteAsync_EmployeeWithoutCurrentPosition_PositionIsNull()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory().WithEmployee(
            userId,
            new EmployeeReference(
                Guid.NewGuid(), "Bruno Costa", EmployeeStatus.Active, null, CurrentPosition: null, userId));
        var useCase = new GetMyProfile(directory);

        var result = await useCase.ExecuteAsync(userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyProfileOutcome.Found, result.Outcome);
        Assert.Null(result.Profile!.CurrentPosition);
    }

    [Fact]
    public async Task ExecuteAsync_NeverReturnsAnotherUsersEmployee()
    {
        // A estrutura já torna isto impossível — não há parâmetro nenhum
        // para pedir "o colaborador de outra pessoa" — mas o teste fixa o
        // comportamento: um utilizador sem colaborador próprio nunca vê o
        // colaborador de outro só porque existe um no directório.
        var outroUserId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory().WithEmployee(
            outroUserId,
            new EmployeeReference(Guid.NewGuid(), "Carla Dias", EmployeeStatus.Active, null, null, outroUserId));
        var useCase = new GetMyProfile(directory);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyProfileOutcome.NotLinked, result.Outcome);
    }
}
