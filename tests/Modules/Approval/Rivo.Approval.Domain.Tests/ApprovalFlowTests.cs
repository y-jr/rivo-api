using Rivo.Approval.Domain;

namespace Rivo.Approval.Domain.Tests;

/// <summary>
/// Percurso de um processo: passos, modos e fecho.
/// </summary>
public class ApprovalFlowTests
{
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly Guid C = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static ApprovalRequest Build(params ResolvedStep[] steps)
    {
        var policy = ApprovalPolicy.Create("hr.leave_request");
        policy.AddStep(Guid.CreateVersion7());

        return ApprovalRequest.Submit(
            "hr.leave_request", "hr", "leave-1", Requester,
            null, null, null, policy, steps, Now);
    }

    private static ResolvedStep Step(int order, StepMode mode, params Guid[] approvers) =>
        new(order, mode, approvers, null);

    [Fact]
    public void Submit_StartsAtFirstStepInProgress()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A));

        Assert.Equal(ApprovalStatus.InProgress, request.Status);
        Assert.Equal(1, request.CurrentStep);
        Assert.Equal(1, request.TotalSteps);
        Assert.Null(request.ClosedAt);
    }

    [Fact]
    public void Approve_TheOnlyStep_ClosesAsApproved()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A));

        request.Decide(A, DecisionAction.Approved, Now);

        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.Equal(Now, request.ClosedAt);
    }

    [Fact]
    public void Approve_AdvancesThroughStepsInOrder()
    {
        var request = Build(
            Step(1, StepMode.AnyApprover, A),
            Step(2, StepMode.AnyApprover, B));

        request.Decide(A, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.InProgress, request.Status);
        Assert.Equal(2, request.CurrentStep);

        request.Decide(B, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
    }

    /// <summary>
    /// `AllApprovers` exige todos os ocupantes — assinatura conjunta, duas
    /// chaves para o mesmo cofre. E a escolha deliberada de quem configura,
    /// nao o comportamento por omissao (ADR-034).
    /// </summary>
    [Fact]
    public void AllApprovers_RequiresEveryApprover()
    {
        var request = Build(Step(1, StepMode.AllApprovers, A, B, C));

        request.Decide(A, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.InProgress, request.Status);
        Assert.Equal(2, request.PendingAssignments.Count);

        request.Decide(B, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.InProgress, request.Status);

        request.Decide(C, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
    }

    /// <summary>
    /// Uma rejeição termina o processo de imediato, em qualquer ponto — não há
    /// "rejeitado mas continua".
    /// </summary>
    [Fact]
    public void Reject_ClosesImmediatelyEvenWithStepsRemaining()
    {
        var request = Build(
            Step(1, StepMode.AnyApprover, A),
            Step(2, StepMode.AnyApprover, B));

        request.Decide(A, DecisionAction.Rejected, Now, "Fora de política");

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
        Assert.Equal(Now, request.ClosedAt);
        Assert.Equal(1, request.CurrentStep);
    }

    [Fact]
    public void Reject_InAllApproversStep_ClosesWithoutWaitingForOthers()
    {
        var request = Build(Step(1, StepMode.AllApprovers, A, B, C));

        request.Decide(A, DecisionAction.Rejected, Now);

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
    }

    [Fact]
    public void Decide_OnClosedRequest_IsRejected()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A), Step(2, StepMode.AnyApprover, B));
        request.Decide(A, DecisionAction.Rejected, Now);

        Assert.Throws<InvalidOperationException>(() =>
            request.Decide(B, DecisionAction.Approved, Now));
    }

    /// <summary>
    /// Pedir esclarecimento suspende sem fechar — e o processo continua a
    /// aceitar decisões dos restantes.
    /// </summary>
    [Fact]
    public void ClarificationRequested_SuspendsWithoutClosing()
    {
        var request = Build(Step(1, StepMode.AllApprovers, A, B));

        request.Decide(A, DecisionAction.ClarificationRequested, Now, "Falta a factura");

        Assert.Equal(ApprovalStatus.ClarificationRequested, request.Status);
        Assert.Null(request.ClosedAt);

        request.Decide(B, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
    }

    [Fact]
    public void Decisions_AreRecordedInOrderWithStepAndAuthor()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A), Step(2, StepMode.AnyApprover, B));

        request.Decide(A, DecisionAction.Approved, Now, "Ok");
        request.Decide(B, DecisionAction.Approved, Now.AddHours(1));

        Assert.Equal(2, request.Decisions.Count);
        Assert.Equal(A, request.Decisions[0].DecidedByEmployeeId);
        Assert.Equal(1, request.Decisions[0].Step);
        Assert.Equal("Ok", request.Decisions[0].Notes);
        Assert.Equal(2, request.Decisions[1].Step);
    }

    [Fact]
    public void Cancel_ClosesAnOpenRequest()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A));

        request.Cancel(Now);

        Assert.Equal(ApprovalStatus.Cancelled, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.Cancel(Now));
    }

    [Fact]
    public void Cancel_AfterClosing_IsRejected()
    {
        var request = Build(Step(1, StepMode.AnyApprover, A));
        request.Decide(A, DecisionAction.Approved, Now);

        Assert.Throws<InvalidOperationException>(() => request.Cancel(Now));
    }

    /// <summary>
    /// <strong>BR-6.</strong> A política fica como rasto, não como referência
    /// viva: alterá-la depois não mexe num processo em curso. O que manda são
    /// as atribuições congeladas.
    /// </summary>
    [Fact]
    public void Assignments_AreFrozenAtSubmission()
    {
        var policy = ApprovalPolicy.Create("hr.leave_request");
        policy.AddStep(Guid.CreateVersion7());

        var request = ApprovalRequest.Submit(
            "hr.leave_request", "hr", "leave-1", Requester,
            null, null, null, policy,
            [Step(1, StepMode.AnyApprover, A)], Now);

        // A política muda depois da submissão.
        policy.AddStep(Guid.CreateVersion7());
        policy.Deactivate();

        // O processo não muda: continua com um passo e um aprovador.
        Assert.Equal(1, request.TotalSteps);
        Assert.Equal(policy.Id, request.AppliedPolicyId);

        request.Decide(A, DecisionAction.Approved, Now);
        Assert.Equal(ApprovalStatus.Approved, request.Status);
    }
}
