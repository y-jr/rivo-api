using Rivo.Approval.Application.UseCases;
using Rivo.Approval.Contracts;
using Rivo.Approval.Domain;
using Rivo.Audit.Contracts;

namespace Rivo.Approval.Application.Tests;

/// <summary>
/// O cancelamento tinha a mesma falha da decisão, pela mesma razão: o
/// colaborador vinha no corpo do pedido (ADR-050).
///
/// <para>
/// Aqui o efeito era o inverso e igualmente mau. O K18 restringe o
/// cancelamento a <strong>quem submeteu</strong> — e essa restrição não vale
/// nada se quem chama puder declarar que é o requisitante. Qualquer conta com
/// <c>approval.requests.read</c> cancelava o pedido de outra pessoa.
/// </para>
/// </summary>
public class CancelRequestTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid ContaDoRequisitante = Guid.CreateVersion7();
    private static readonly Guid Requisitante = Guid.CreateVersion7();

    private static readonly Guid ContaDeOutro = Guid.CreateVersion7();
    private static readonly Guid Outro = Guid.CreateVersion7();

    private static ApprovalRequest PedidoPendente()
    {
        var policy = ApprovalPolicy.Create("hr.leave_request");
        policy.AddStep(Guid.CreateVersion7());

        return ApprovalRequest.Submit(
            "hr.leave_request", "hr", "leave-42", Requisitante,
            amount: null, currency: null, departmentId: null,
            policy,
            [new ResolvedStep(1, StepMode.AnyApprover, [Outro], null)],
            Agora);
    }

    private static (CancelRequest CasoDeUso, FakeApprovalStore Store, FakeAuditTrail Trilha)
        Montar(ApprovalRequest pedido, FakeEmployeeDirectory colaboradores)
    {
        var store = new FakeApprovalStore(pedido);
        var trilha = new FakeAuditTrail();

        return (
            new CancelRequest(store, colaboradores, trilha, new RelogioFixo(Agora)),
            store,
            trilha);
    }

    private static AuditContext Contexto(Guid conta) => new(conta, "::1", "teste");

    [Fact]
    public async Task Cancelar_PeloRequisitante_Aceite()
    {
        var pedido = PedidoPendente();
        var colaboradores = new FakeEmployeeDirectory().ComVinculo(ContaDoRequisitante, Requisitante);
        var (casoDeUso, store, _) = Montar(pedido, colaboradores);

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, ContaDoRequisitante, Contexto(ContaDoRequisitante), CancellationToken.None);

        Assert.Equal(DecisionOutcome.Recorded, resultado.Outcome);
        Assert.Equal(ApprovalStatus.Cancelled, pedido.Status);
        Assert.Equal(1, store.SaveCount);
    }

    /// <summary>
    /// **O caso que a correcção fecha.** Antes, esta conta cancelava o pedido
    /// alheio indicando o identificador do requisitante no corpo. Agora o
    /// colaborador vem do vínculo dela, e o K18 aplica-se a quem chama.
    /// </summary>
    [Fact]
    public async Task Cancelar_PorOutraPessoa_Recusado()
    {
        var pedido = PedidoPendente();
        var colaboradores = new FakeEmployeeDirectory()
            .ComVinculo(ContaDoRequisitante, Requisitante)
            .ComVinculo(ContaDeOutro, Outro);

        var (casoDeUso, _, trilha) = Montar(pedido, colaboradores);

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, ContaDeOutro, Contexto(ContaDeOutro), CancellationToken.None);

        Assert.Equal(DecisionOutcome.SegregationViolation, resultado.Outcome);
        Assert.NotEqual(ApprovalStatus.Cancelled, pedido.Status);
        Assert.Contains(
            trilha.Registos,
            r => r.Action == ApprovalAuditActions.SegregationViolationAttempted);
    }

    [Fact]
    public async Task Cancelar_SemColaboradorAssociado_Recusado()
    {
        var pedido = PedidoPendente();
        var contaSolta = Guid.CreateVersion7();
        var (casoDeUso, store, _) = Montar(pedido, new FakeEmployeeDirectory());

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, contaSolta, Contexto(contaSolta), CancellationToken.None);

        Assert.Equal(DecisionOutcome.SegregationViolation, resultado.Outcome);
        Assert.NotEqual(ApprovalStatus.Cancelled, pedido.Status);
        Assert.Equal(0, store.SaveCount);
    }
}
