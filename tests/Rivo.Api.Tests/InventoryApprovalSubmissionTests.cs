using Rivo.Api.Composition;
using Rivo.Approval.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Inventory.Application.Abstractions;

namespace Rivo.Api.Tests;

/// <summary>
/// O adaptador que liga as divergências de contagem à governança (ADR-064).
///
/// <para>
/// <strong>Existe por causa de uma volta de CI.</strong> A primeira versão
/// exigia o colaborador ligado <em>antes</em> de perguntar se havia sequer
/// alçada configurada — e assim uma conta administrativa sem ficha de pessoal,
/// como o Admin de arranque, deixava de conseguir fechar contagens num sistema
/// onde ninguém tinha configurado governança nenhuma. A ordem das duas
/// perguntas é a decisão inteira deste adaptador, e é isso que estes testes
/// prendem.
/// </para>
/// </summary>
public class InventoryApprovalSubmissionTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Conta = Guid.CreateVersion7();

    private sealed class CatalogoFalso(params ApprovalPolicySummary[] politicas) : IApprovalPolicyCatalogue
    {
        public Task<IReadOnlyList<ApprovalPolicySummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApprovalPolicySummary>>([.. politicas]);
    }

    private sealed class GatewayFalso(SubmissionResult? resposta = null) : IApprovalGateway
    {
        public int Submissoes { get; private set; }

        public Task<SubmissionResult> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken)
        {
            Submissoes++;
            return Task.FromResult(resposta ?? SubmissionResult.Submitted(Guid.CreateVersion7()));
        }

        public Task<ApprovalStatusView?> GetStatusAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalStatusView?>(null);
    }

    private sealed class DirectorioFalso(EmployeeReference? colaborador) : IEmployeeDirectory
    {
        public int Consultas { get; private set; }

        public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken)
        {
            Consultas++;
            return Task.FromResult(colaborador);
        }

        public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeReference?>(null);

        public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmployeeReference>>([]);

        public Task<EmployeeHireResult> HireAsync(string fullName, string? departmentName, DateTimeOffset hiredOn, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult(EmployeeHireResult.Success(Guid.CreateVersion7()));
    }

    private static ApprovalPolicySummary Politica(string processo, bool activa = true) =>
        new(Guid.CreateVersion7(), processo, activa, StepCount: 1, RequiresBudgetCheck: false);

    private static EmployeeReference Colaborador() =>
        new(Guid.CreateVersion7(), "Ana Silva", EmployeeStatus.Active, null, null, Conta);

    [Fact]
    public async Task SemAlcadaConfigurada_NaoPrecisaDeColaboradorNemSubmete()
    {
        var gateway = new GatewayFalso();
        var directorio = new DirectorioFalso(null);
        var adaptador = new InventoryApprovalSubmission(
            gateway, new CatalogoFalso(), directorio, new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.NoApplicablePolicy, resultado.Outcome);

        // Sem alçada, nem se pergunta quem requer: exigi-lo impedia uma conta
        // administrativa de fechar contagens num sistema sem governança.
        Assert.Equal(0, directorio.Consultas);
        Assert.Equal(0, gateway.Submissoes);
    }

    [Fact]
    public async Task PoliticaDeOutroProcesso_NaoConta()
    {
        var directorio = new DirectorioFalso(null);
        var adaptador = new InventoryApprovalSubmission(
            new GatewayFalso(),
            new CatalogoFalso(Politica(ApprovalProcessTypes.PayrollRun)),
            directorio,
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.NoApplicablePolicy, resultado.Outcome);
        Assert.Equal(0, directorio.Consultas);
    }

    [Fact]
    public async Task PoliticaInactiva_NaoConta()
    {
        var adaptador = new InventoryApprovalSubmission(
            new GatewayFalso(),
            new CatalogoFalso(Politica(ApprovalProcessTypes.StockCount, activa: false)),
            new DirectorioFalso(null),
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.NoApplicablePolicy, resultado.Outcome);
    }

    [Fact]
    public async Task ComAlcadaESemColaboradorLigado_Bloqueia()
    {
        var gateway = new GatewayFalso();
        var adaptador = new InventoryApprovalSubmission(
            gateway,
            new CatalogoFalso(Politica(ApprovalProcessTypes.StockCount)),
            new DirectorioFalso(null),
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        // Há governança e não se pode cumprir: não se aplica a correcção em
        // silêncio só porque quem fechou não tem ficha (BR-2).
        Assert.Equal(InventoryApprovalOutcome.Blocked, resultado.Outcome);
        Assert.Contains("colaborador", resultado.Reason);
        Assert.Equal(0, gateway.Submissoes);
    }

    [Fact]
    public async Task ComAlcadaEComColaborador_SubmeteComOValorDaDivergencia()
    {
        var gateway = new GatewayFalso();
        var adaptador = new InventoryApprovalSubmission(
            gateway,
            new CatalogoFalso(Politica(ApprovalProcessTypes.StockCount)),
            new DirectorioFalso(Colaborador()),
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.Submitted, resultado.Outcome);
        Assert.NotNull(resultado.RequestId);
        Assert.Equal(1, gateway.Submissoes);
    }

    /// <summary>
    /// A leitura que distingue este módulo de `payroll`: para uma folha, não
    /// haver política é configuração em falta; para uma contagem, é a resposta
    /// que diz «isto não precisa de aprovação».
    /// </summary>
    [Fact]
    public async Task GatewayRecusaPorFaltaDePolitica_LeSeComoNaoPrecisaDeAprovacao()
    {
        var adaptador = new InventoryApprovalSubmission(
            new GatewayFalso(SubmissionResult.NoPolicy("Nenhuma faixa cobre este valor.")),
            new CatalogoFalso(Politica(ApprovalProcessTypes.StockCount)),
            new DirectorioFalso(Colaborador()),
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 5m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.NoApplicablePolicy, resultado.Outcome);
    }

    [Fact]
    public async Task GatewayRecusaPorAmbiguidade_Bloqueia()
    {
        var adaptador = new InventoryApprovalSubmission(
            new GatewayFalso(SubmissionResult.AmbiguousPolicy("Duas políticas igualmente aplicáveis.")),
            new CatalogoFalso(Politica(ApprovalProcessTypes.StockCount)),
            new DirectorioFalso(Colaborador()),
            new RelogioFixo(Agora));

        var resultado = await adaptador.SubmitAsync(
            Guid.CreateVersion7(), Conta, 10_000m, "Contagem", CancellationToken.None);

        Assert.Equal(InventoryApprovalOutcome.Blocked, resultado.Outcome);
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
