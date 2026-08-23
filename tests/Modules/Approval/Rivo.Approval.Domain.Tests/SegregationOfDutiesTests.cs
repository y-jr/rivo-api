using Rivo.Approval.Domain;

namespace Rivo.Approval.Domain.Tests;

/// <summary>
/// As invariantes de segregação de funções.
///
/// <para>
/// <strong>É o teste mais importante do projecto inteiro.</strong> O ADR-008
/// fixa que a sede destas regras é o domínio `approval`, em código, e que uma
/// regra que só exista em SQL é defeito de arquitectura. Estes casos são a
/// prova de que existem aqui.
/// </para>
/// </summary>
public class SegregationOfDutiesTests
{
    private static readonly Guid Requester = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();
    private static readonly Guid Director = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static ApprovalPolicy Policy(params Guid[] positions)
    {
        var policy = ApprovalPolicy.Create("hr.leave_request");

        foreach (var position in positions.DefaultIfEmpty(Guid.CreateVersion7()))
        {
            policy.AddStep(position);
        }

        return policy;
    }

    private static ApprovalRequest Request(params Guid[][] approversPerStep)
    {
        var steps = approversPerStep
            .Select((approvers, index) => new ResolvedStep(index + 1, StepMode.Sequential, approvers, null))
            .ToList();

        return ApprovalRequest.Submit(
            "hr.leave_request", "hr", "leave-42", Requester,
            amount: null, currency: null, departmentId: null,
            Policy(), steps, Now);
    }

    /// <summary>
    /// <strong>BR-2.</strong> Quem submete nunca decide sobre o próprio pedido.
    ///
    /// <para>
    /// Vale mesmo que o requisitante esteja atribuído ao passo — o que acontece
    /// quando alguém ocupa o cargo que aprova os seus próprios pedidos.
    /// </para>
    /// </summary>
    [Fact]
    public void Decide_ByTheRequester_ViolatesSegregation()
    {
        var request = Request([Requester, Manager]);

        var error = Assert.Throws<SegregationOfDutiesException>(() =>
            request.Decide(Requester, DecisionAction.Approved, Now));

        Assert.Contains("BR-2", error.Message);
        Assert.Equal(ApprovalStatus.InProgress, request.Status);
        Assert.Empty(request.Decisions);
    }

    /// <summary>
    /// <strong>BR-4.</strong> Quem já interveio não decide outra vez.
    ///
    /// <para>
    /// Sem isto, quem ocupasse os dois cargos de um workflow de dois passos
    /// satisfá-lo-ia sozinho — a acumulação de papéis conflituantes que BR-3 e
    /// BR-4 existem para impedir.
    /// </para>
    /// </summary>
    [Fact]
    public void Decide_TwiceBySamePerson_ViolatesSegregation()
    {
        var request = Request([Manager], [Manager, Director]);

        request.Decide(Manager, DecisionAction.Approved, Now);
        Assert.Equal(2, request.CurrentStep);

        var error = Assert.Throws<SegregationOfDutiesException>(() =>
            request.Decide(Manager, DecisionAction.Approved, Now));

        Assert.Contains("BR-4", error.Message);

        // O processo não avançou por causa da tentativa.
        Assert.Equal(ApprovalStatus.InProgress, request.Status);
        Assert.Single(request.Decisions);
    }

    [Fact]
    public void Decide_ByeSomeoneNotAssigned_IsRejected()
    {
        var request = Request([Manager]);

        Assert.Throws<InvalidOperationException>(() =>
            request.Decide(Director, DecisionAction.Approved, Now));
    }

    /// <summary>
    /// Um aprovador do passo 2 não decide enquanto o passo 1 estiver aberto.
    /// </summary>
    [Fact]
    public void Decide_OutOfStepOrder_IsRejected()
    {
        var request = Request([Manager], [Director]);

        Assert.Throws<InvalidOperationException>(() =>
            request.Decide(Director, DecisionAction.Approved, Now));
    }

    /// <summary>
    /// <strong>BR-6.</strong> Sem aprovadores resolvidos não há submissão — um
    /// processo assim ficaria em silêncio à espera de quem não existe.
    /// </summary>
    [Fact]
    public void Submit_WithoutResolvedApprovers_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Request([]));
    }
}
