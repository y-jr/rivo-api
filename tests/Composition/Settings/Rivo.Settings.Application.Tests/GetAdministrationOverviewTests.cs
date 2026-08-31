using Rivo.Approval.Contracts;
using Rivo.Identity.Contracts;

namespace Rivo.Settings.Application.Tests;

public class GetAdministrationOverviewTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsProfilesSortedByName()
    {
        var profiles = new FakeAccessProfileCatalogue(
            new AccessProfileSummary("Sales", ["commercial.customers.read"]),
            new AccessProfileSummary("Admin", ["identity.roles.read"]));
        var useCase = new GetAdministrationOverview(profiles, new FakeApprovalPolicyCatalogue());

        var overview = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["Admin", "Sales"], overview.AccessProfiles.Select(p => p.Name));
    }

    [Fact]
    public async Task ExecuteAsync_GroupsApprovalRulesByTheModulePrefixOfProcessType()
    {
        var policyId = Guid.NewGuid();
        var policies = new FakeApprovalPolicyCatalogue(
            new ApprovalPolicySummary(policyId, "hr.leave_request", true, 1, false),
            new ApprovalPolicySummary(Guid.NewGuid(), "finance.payment_request", true, 2, true));
        var useCase = new GetAdministrationOverview(new FakeAccessProfileCatalogue(), policies);

        var overview = await useCase.ExecuteAsync(CancellationToken.None);

        // Ordem alfabética do módulo, não da ordem em que `approval` devolveu.
        Assert.Equal(["finance", "hr"], overview.ApprovalRulesByModule.Select(g => g.Module));

        var hr = overview.ApprovalRulesByModule.Single(g => g.Module == "hr");
        var rule = Assert.Single(hr.Rules);
        Assert.Equal(policyId, rule.PolicyId);
        Assert.Equal("hr.leave_request", rule.ProcessType);
        Assert.Equal(1, rule.StepCount);
        Assert.False(rule.RequiresBudgetCheck);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesInactivePolicies_DoesNotHideThem()
    {
        // Uma política desactivada continua a ser governança em vigor até
        // ontem — escondê-la da vista de administração esconderia o próprio
        // histórico de configuração, não só a política.
        var policies = new FakeApprovalPolicyCatalogue(
            new ApprovalPolicySummary(Guid.NewGuid(), "procurement.purchase_requisition", false, 1, false));
        var useCase = new GetAdministrationOverview(new FakeAccessProfileCatalogue(), policies);

        var overview = await useCase.ExecuteAsync(CancellationToken.None);

        var rule = Assert.Single(overview.ApprovalRulesByModule.Single().Rules);
        Assert.False(rule.IsActive);
    }

    [Fact]
    public async Task ExecuteAsync_NoPolicies_ReturnsNoModuleGroups()
    {
        var useCase = new GetAdministrationOverview(
            new FakeAccessProfileCatalogue(), new FakeApprovalPolicyCatalogue());

        var overview = await useCase.ExecuteAsync(CancellationToken.None);

        Assert.Empty(overview.ApprovalRulesByModule);
    }
}
