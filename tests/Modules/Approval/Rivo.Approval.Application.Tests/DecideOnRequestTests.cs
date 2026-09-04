using Rivo.Approval.Application.UseCases;
using Rivo.Approval.Contracts;
using Rivo.Approval.Domain;
using Rivo.Audit.Contracts;

namespace Rivo.Approval.Application.Tests;

/// <summary>
/// <strong>Quem decide é a conta autenticada, nunca o corpo do pedido</strong>
/// (ADR-050).
///
/// <para>
/// A falha que estes testes fecham: até 2026-09-04, o identificador do
/// colaborador chegava no corpo HTTP e ninguém o confrontava com quem
/// chamava. As regras de segregação — que o domínio impõe correctamente —
/// eram aplicadas ao colaborador **declarado**. Quem tivesse
/// <c>approval.requests.decide</c> aprovava o seu próprio pedido indicando o
/// identificador de outra pessoa.
/// </para>
///
/// <para>
/// Nenhum teste de domínio podia apanhar isto: o domínio recebia o
/// identificador já escolhido e fazia o que devia com ele. O defeito estava
/// em quem escolhia, que é orquestração — e é por isso que este projecto de
/// testes nasceu.
/// </para>
/// </summary>
public class DecideOnRequestTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid ContaDoRequisitante = Guid.CreateVersion7();
    private static readonly Guid Requisitante = Guid.CreateVersion7();

    private static readonly Guid ContaDoAprovador = Guid.CreateVersion7();
    private static readonly Guid Aprovador = Guid.CreateVersion7();

    private static ApprovalRequest PedidoPendente()
    {
        var policy = ApprovalPolicy.Create("hr.leave_request");
        policy.AddStep(Guid.CreateVersion7());

        return ApprovalRequest.Submit(
            "hr.leave_request", "hr", "leave-42", Requisitante,
            amount: null, currency: null, departmentId: null,
            policy,
            [new ResolvedStep(1, StepMode.AnyApprover, [Requisitante, Aprovador], null)],
            Agora);
    }

    private static (DecideOnRequest CasoDeUso, FakeApprovalStore Store, FakeAuditTrail Trilha)
        Montar(ApprovalRequest pedido, FakeEmployeeDirectory colaboradores)
    {
        var store = new FakeApprovalStore(pedido);
        var trilha = new FakeAuditTrail();
        var relogio = new RelogioFixo(Agora);

        return (new DecideOnRequest(store, colaboradores, trilha, relogio), store, trilha);
    }

    private static AuditContext Contexto(Guid conta) => new(conta, "::1", "teste");

    [Fact]
    public async Task Decide_RegistaODecisorDaContaAutenticada()
    {
        var pedido = PedidoPendente();
        var colaboradores = new FakeEmployeeDirectory().ComVinculo(ContaDoAprovador, Aprovador);
        var (casoDeUso, store, _) = Montar(pedido, colaboradores);

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, ContaDoAprovador, "Approved", notes: null,
            Contexto(ContaDoAprovador), CancellationToken.None);

        Assert.Equal(DecisionOutcome.Recorded, resultado.Outcome);
        Assert.Equal(1, store.SaveCount);

        // O decisor registado é o colaborador do vínculo, e não algo que o
        // chamador tenha podido escolher — porque já não há por onde o
        // escolher.
        var decisao = Assert.Single(pedido.Decisions);
        Assert.Equal(Aprovador, decisao.DecidedByEmployeeId);
    }

    /// <summary>
    /// **O caso que dá nome ao ADR-050.** O requisitante tenta decidir sobre o
    /// próprio pedido. Antes, bastava-lhe indicar o identificador do aprovador
    /// no corpo; agora o colaborador vem do vínculo da conta dele, e BR-2
    /// aplica-se a quem está mesmo a chamar.
    /// </summary>
    [Fact]
    public async Task Decide_PeloRequisitante_ViolaSegregacao_MesmoQuerendoPassarPorOutro()
    {
        var pedido = PedidoPendente();
        var colaboradores = new FakeEmployeeDirectory()
            .ComVinculo(ContaDoRequisitante, Requisitante)
            .ComVinculo(ContaDoAprovador, Aprovador);

        var (casoDeUso, _, trilha) = Montar(pedido, colaboradores);

        // A conta é a do requisitante. Não há nenhum parâmetro por onde ele
        // possa dizer que é outra pessoa — e é esse o ponto da correcção.
        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, ContaDoRequisitante, "Approved", notes: null,
            Contexto(ContaDoRequisitante), CancellationToken.None);

        Assert.Equal(DecisionOutcome.SegregationViolation, resultado.Outcome);
        Assert.Empty(pedido.Decisions);

        // A tentativa recusada vai para a trilha: uma sequência destas contra
        // o mesmo pedido é o padrão que interessa detectar.
        Assert.Contains(
            trilha.Registos,
            r => r.Action == ApprovalAuditActions.SegregationViolationAttempted);
    }

    [Fact]
    public async Task Decide_SemColaboradorAssociado_NaoDecide()
    {
        var pedido = PedidoPendente();
        var contaSolta = Guid.CreateVersion7();
        var (casoDeUso, store, _) = Montar(pedido, new FakeEmployeeDirectory());

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, contaSolta, "Approved", notes: null,
            Contexto(contaSolta), CancellationToken.None);

        Assert.Equal(DecisionOutcome.SegregationViolation, resultado.Outcome);
        Assert.Empty(pedido.Decisions);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Decide_ComAccaoDesconhecida_Recusa()
    {
        var pedido = PedidoPendente();
        var colaboradores = new FakeEmployeeDirectory().ComVinculo(ContaDoAprovador, Aprovador);
        var (casoDeUso, _, _) = Montar(pedido, colaboradores);

        var resultado = await casoDeUso.ExecuteAsync(
            pedido.Id, ContaDoAprovador, "Aprovar", notes: null,
            Contexto(ContaDoAprovador), CancellationToken.None);

        Assert.Equal(DecisionOutcome.Rejected, resultado.Outcome);
    }

    [Fact]
    public async Task Decide_PedidoInexistente_DevolveNaoEncontrado()
    {
        var colaboradores = new FakeEmployeeDirectory().ComVinculo(ContaDoAprovador, Aprovador);
        var (casoDeUso, _, _) = Montar(PedidoPendente(), colaboradores);

        var resultado = await casoDeUso.ExecuteAsync(
            Guid.CreateVersion7(), ContaDoAprovador, "Approved", notes: null,
            Contexto(ContaDoAprovador), CancellationToken.None);

        Assert.Equal(DecisionOutcome.NotFound, resultado.Outcome);
    }
}
