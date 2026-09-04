using Rivo.Approval.Application.Abstractions;
using Rivo.Approval.Domain;
using Rivo.Audit.Contracts;
using Rivo.Hr.Contracts;

namespace Rivo.Approval.Application.Tests;

/// <summary>Duplos escritos à mão, sem biblioteca de mocks — ADR-022.</summary>
internal sealed class FakeApprovalStore(ApprovalRequest? request = null) : IApprovalStore
{
    public int SaveCount { get; private set; }

    public Task<ApprovalRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(request is not null && request.Id == requestId ? request : null);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesForProcessAsync(
        string processType, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovalPolicy>>([]);

    public Task<IReadOnlyList<ApprovalPolicy>> ListPoliciesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovalPolicy>>([]);

    public Task AddPolicyAsync(ApprovalPolicy policy, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<ApprovalPolicy?> FindPolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalPolicy?>(null);

    public Task<IReadOnlyList<ApprovalRequest>> ListRequestsAsync(
        string? processType, Guid? pendingForEmployeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApprovalRequest>>([]);

    public Task AddRequestAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Directório de colaboradores com um vínculo conta → colaborador de cada vez.
///
/// É o duplo que interessa a estes testes: a correcção do ADR-050 consiste
/// precisamente em o caso de uso passar por aqui em vez de aceitar um
/// identificador vindo de fora.
/// </summary>
internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly Dictionary<Guid, Guid> _colaboradorPorConta = [];

    /// <summary>Liga uma conta de `identity` a um Colaborador de `hr`.</summary>
    public FakeEmployeeDirectory ComVinculo(Guid userId, Guid employeeId)
    {
        _colaboradorPorConta[userId] = employeeId;
        return this;
    }

    public Task<EmployeeReference?> FindByUserIdAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (!_colaboradorPorConta.TryGetValue(userId, out var employeeId))
        {
            return Task.FromResult<EmployeeReference?>(null);
        }

        return Task.FromResult<EmployeeReference?>(new EmployeeReference(
            employeeId, "Colaborador de teste", EmployeeStatus.Active, null, null, userId));
    }

    public Task<EmployeeReference?> FindAsync(
        Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<EmployeeReference?>(null);

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(
        Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeReference>>([]);

    public Task<EmployeeHireResult> HireAsync(
        string fullName, string? departmentName, DateTimeOffset hiredOn, Guid actorId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado por approval.");
}

/// <summary>Guarda o que foi auditado, para os testes que verificam o rasto.</summary>
internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Relógio parado numa data.
///
/// Escrito à mão em vez de trazer `Microsoft.Extensions.TimeProvider.Testing`:
/// o que estes testes precisam de um relógio é que não se mexa, e isso são
/// três linhas (ADR-022 — duplos à mão, sem biblioteca).
/// </summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
