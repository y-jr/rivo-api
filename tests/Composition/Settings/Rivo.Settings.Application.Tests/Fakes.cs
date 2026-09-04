using Rivo.Approval.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Identity.Contracts;
using Rivo.Procurement.Contracts;

namespace Rivo.Settings.Application.Tests;

/// <summary>
/// Duplos escritos à mão, sem biblioteca de mocks — ADR-022. Os dois
/// contratos que <see cref="GetAdministrationOverview"/> compõe são
/// interfaces simples, sem estado interno a simular além do que o teste
/// fornece.
/// </summary>
internal sealed class FakeAccessProfileCatalogue : IAccessProfileCatalogue
{
    private readonly IReadOnlyList<AccessProfileSummary> _profiles;

    public FakeAccessProfileCatalogue(params AccessProfileSummary[] profiles) => _profiles = profiles;

    public IReadOnlyList<AccessProfileSummary> List() => _profiles;
}

internal sealed class FakeApprovalPolicyCatalogue : IApprovalPolicyCatalogue
{
    private readonly IReadOnlyList<ApprovalPolicySummary> _policies;

    public FakeApprovalPolicyCatalogue(params ApprovalPolicySummary[] policies) => _policies = policies;

    public Task<IReadOnlyList<ApprovalPolicySummary>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_policies);
}

/// <summary>
/// Duplo de <see cref="ICustomerDirectory"/> para os testes de importação
/// CSV — simula a mesma verificação de NIF duplicado do caso de uso real,
/// sem repetir o agregado inteiro.
/// </summary>
internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    private readonly Dictionary<string, Guid> _byTaxId = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Name, string TaxId, string? Email, string? Phone, Guid ActorId)> Registered { get; } = [];

    public Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(null);

    public Task<CustomerReference?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(null);

    public Task<CustomerRegistrationResult> RegisterAsync(
        string name, string taxId, string addressDetail, string city, string country,
        string? email, string? phone, Guid actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(taxId))
        {
            return Task.FromResult(CustomerRegistrationResult.Rejected("Nome e NIF são obrigatórios."));
        }

        if (_byTaxId.TryGetValue(taxId, out var existingId))
        {
            return Task.FromResult(CustomerRegistrationResult.Duplicate(existingId));
        }

        var id = Guid.CreateVersion7();
        _byTaxId[taxId] = id;
        Registered.Add((name, taxId, email, phone, actorId));

        return Task.FromResult(CustomerRegistrationResult.Success(id));
    }
}

/// <summary>Duplo de <see cref="IEmployeeDirectory"/> para os testes de importação CSV.</summary>
internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly HashSet<string> _departments;

    public FakeEmployeeDirectory(params string[] departments) =>
        _departments = new HashSet<string>(departments, StringComparer.OrdinalIgnoreCase);

    public List<(string FullName, string? DepartmentName, DateTimeOffset HiredOn, Guid ActorId)> Hired { get; } = [];

    public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<EmployeeReference?>(null);

    public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<EmployeeReference?>(null);

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(
        Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeReference>>([]);

    public Task<EmployeeHireResult> HireAsync(
        string fullName, string? departmentName, DateTimeOffset hiredOn, Guid actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Task.FromResult(EmployeeHireResult.Rejected("Um colaborador precisa de nome."));
        }

        if (departmentName is not null && !_departments.Contains(departmentName))
        {
            return Task.FromResult(EmployeeHireResult.DepartmentNotFound(departmentName));
        }

        Hired.Add((fullName, departmentName, hiredOn, actorId));

        return Task.FromResult(EmployeeHireResult.Success(Guid.CreateVersion7()));
    }
}

/// <summary>Duplo de <see cref="ISupplierDirectory"/> para os testes de importação CSV.</summary>
internal sealed class FakeSupplierDirectory : ISupplierDirectory
{
    private readonly Dictionary<string, Guid> _byTaxId = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Name, string TaxId, string? Iban, Guid ActorId)> Registered { get; } = [];

    public Task<SupplierReference?> FindAsync(Guid supplierId, CancellationToken cancellationToken) =>
        Task.FromResult<SupplierReference?>(null);

    public Task<SupplierReference?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken) =>
        Task.FromResult<SupplierReference?>(null);

    public Task<SupplierRegistrationResult> RegisterAsync(
        string name, string taxId, string? iban, string? email, string? phone,
        Guid actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(taxId))
        {
            return Task.FromResult(SupplierRegistrationResult.Rejected("Nome e NIF são obrigatórios."));
        }

        if (_byTaxId.TryGetValue(taxId, out var existingId))
        {
            return Task.FromResult(SupplierRegistrationResult.Duplicate(existingId));
        }

        var id = Guid.CreateVersion7();
        _byTaxId[taxId] = id;
        Registered.Add((name, taxId, iban, actorId));

        return Task.FromResult(SupplierRegistrationResult.Success(id));
    }
}
