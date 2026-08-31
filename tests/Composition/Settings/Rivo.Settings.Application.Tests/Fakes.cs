using Rivo.Approval.Contracts;
using Rivo.Identity.Contracts;

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
