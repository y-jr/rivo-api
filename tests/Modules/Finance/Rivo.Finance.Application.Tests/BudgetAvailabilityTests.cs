using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// BR-8 — a verificação orçamental antes da decisão.
///
/// <para>
/// <strong>Nada disto é verificável no domínio.</strong> A regra atravessa
/// três coisas que nenhum agregado vê ao mesmo tempo: a tradução departamento →
/// centro de custo, o orçamento em vigor daquele mês, e quanto já está
/// comprometido. É orquestração pura, e até aqui não tinha teste nenhum.
/// </para>
///
/// <para>
/// O que mais interessa fixar é <strong>que só um dos cinco resultados deixa
/// passar</strong>. Todos os outros recusam, incluindo os que significam "não
/// consegui verificar" — aprovar por omissão é o modo de falha que BR-8 existe
/// para impedir.
/// </para>
/// </summary>
public class BudgetAvailabilityTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 8, 25);

    private static readonly Guid Departamento = Guid.CreateVersion7();

    private static CostCentre Centro(Guid? departamento = null, string code = "CC-OPS") =>
        CostCentre.Open(code, "Operações", departamento ?? Departamento, Guid.CreateVersion7());

    private static Budget OrcamentoAprovado(
        Guid costCentreId,
        decimal tectoDeAgosto = 500_000m,
        string moeda = "AOA")
    {
        var orcamento = Budget.Draft(costCentreId, 2026, moeda);
        orcamento.SetMonth(8, tectoDeAgosto);
        orcamento.Approve(Guid.CreateVersion7(), Agora);

        return orcamento;
    }

    private static BudgetCheck Pergunta(
        decimal montante = 100_000m,
        string? rubrica = null,
        Guid? departamento = null,
        string moeda = "AOA") =>
        new(rubrica, departamento ?? Departamento, montante, moeda, Hoje);

    // ---- cabe ----

    [Fact]
    public async Task ValorQueCabeNoTecto_Passa()
    {
        var centro = Centro();
        var store = new FakePlanningStore().With(centro).With(OrcamentoAprovado(centro.Id));

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(100_000m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.Within, resultado.Outcome);
        Assert.Equal(500_000m, resultado.Ceiling);
        Assert.Equal(0m, resultado.Committed);
        Assert.Equal(500_000m, resultado.Available);
    }

    /// <summary>
    /// Exactamente o tecto cabe. A regra é "não exceder", não "ficar abaixo".
    /// </summary>
    [Fact]
    public async Task ValorExactamenteIgualAoDisponivel_Passa()
    {
        var centro = Centro();
        var store = new FakePlanningStore()
            .With(centro)
            .With(OrcamentoAprovado(centro.Id, 500_000m))
            .WithCommitted(centro.Id, 2026, 8, 400_000m);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(100_000m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.Within, resultado.Outcome);
        Assert.Equal(100_000m, resultado.Available);
    }

    // ---- não cabe ----

    /// <summary>
    /// <strong>É o que a regra existe para fazer.</strong> O comprometido conta
    /// — não é o tecto contra o pedido, é o que resta contra o pedido.
    /// </summary>
    [Fact]
    public async Task OComprometidoContaContraOTecto()
    {
        var centro = Centro();
        var store = new FakePlanningStore()
            .With(centro)
            .With(OrcamentoAprovado(centro.Id, 500_000m))
            .WithCommitted(centro.Id, 2026, 8, 450_000m);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(100_000m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.Exceeded, resultado.Outcome);
        Assert.Equal(50_000m, resultado.Available);
        Assert.Contains("450", resultado.Reason);
    }

    /// <summary>
    /// O orçamento é <strong>mensal</strong>. O que se comprometeu noutro mês
    /// não consome este — senão o tecto anual seria o único que contava.
    /// </summary>
    [Fact]
    public async Task OComprometidoDeOutroMes_NaoConsomeEste()
    {
        var centro = Centro();
        var store = new FakePlanningStore()
            .With(centro)
            .With(OrcamentoAprovado(centro.Id, 500_000m))
            .WithCommitted(centro.Id, 2026, 7, 500_000m);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(400_000m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.Within, resultado.Outcome);
    }

    // ---- não consegui verificar ----

    /// <summary>
    /// Um rascunho não controla nada. Verificar contra números que ninguém
    /// aprovou seria dar uma resposta sem valor.
    /// </summary>
    [Fact]
    public async Task OrcamentoEmRascunho_NaoVerifica()
    {
        var centro = Centro();
        var rascunho = Budget.Draft(centro.Id, 2026, "AOA");
        rascunho.SetMonth(8, 9_000_000m);

        var store = new FakePlanningStore().With(centro).With(rascunho);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(100_000m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoBudget, resultado.Outcome);
        Assert.Contains("rascunho", resultado.Reason);
    }

    [Fact]
    public async Task SemOrcamentoParaOAno_NaoVerifica()
    {
        var centro = Centro();
        var store = new FakePlanningStore().With(centro);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoBudget, resultado.Outcome);
    }

    /// <summary>
    /// "Não orçamentado" é diferente de "orçamentado a zero". Um mês sem tecto
    /// não é um tecto de zero — é a ausência de resposta.
    /// </summary>
    [Fact]
    public async Task MesSemTecto_NaoVerifica()
    {
        var centro = Centro();
        var orcamento = Budget.Draft(centro.Id, 2026, "AOA");
        orcamento.SetMonth(1, 500_000m);
        orcamento.Approve(Guid.CreateVersion7(), Agora);

        var store = new FakePlanningStore().With(centro).With(orcamento);

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoBudget, resultado.Outcome);
    }

    [Fact]
    public async Task SemDepartamentoNemRubrica_NaoVerifica()
    {
        var resultado = await new BudgetAvailability(new FakePlanningStore())
            .CheckAsync(new BudgetCheck(null, null, 1m, "AOA", Hoje), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
    }

    /// <summary>
    /// O mapeamento departamento → centro de custo é opcional por desenho (D4).
    /// A sua ausência é um estado normal — e mesmo assim recusa, porque não há
    /// contra que verificar.
    /// </summary>
    [Fact]
    public async Task DepartamentoSemCentroDeCusto_NaoVerifica()
    {
        var store = new FakePlanningStore().With(Centro(Guid.CreateVersion7()));

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
    }

    /// <summary>
    /// <strong>D4 diz que o mapeamento não é 1:1.</strong> Dois centros no
    /// mesmo departamento é estado legítimo — o que não é legítimo é escolher
    /// um, porque seria verificar contra um tecto que ninguém indicou.
    /// </summary>
    [Fact]
    public async Task DoisCentrosNoMesmoDepartamento_RecusaEmVezDeEscolher()
    {
        var primeiro = Centro(code: "CC-OPS");
        var segundo = Centro(code: "CC-LOG");

        var store = new FakePlanningStore()
            .With(primeiro)
            .With(segundo)
            .With(OrcamentoAprovado(primeiro.Id, 9_000_000m));

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
        Assert.Contains("rubrica", resultado.Reason);
    }

    /// <summary>
    /// Com a rubrica indicada não há ambiguidade nenhuma a resolver — e é para
    /// isso que ela atravessa `approval` sem ser interpretada.
    /// </summary>
    [Fact]
    public async Task RubricaIndicada_DesfazAAmbiguidade()
    {
        var primeiro = Centro(code: "CC-OPS");
        var segundo = Centro(code: "CC-LOG");

        var store = new FakePlanningStore()
            .With(primeiro)
            .With(segundo)
            .With(OrcamentoAprovado(segundo.Id, 500_000m));

        var resultado = await new BudgetAvailability(store).CheckAsync(
            Pergunta(100_000m, rubrica: segundo.Id.ToString()), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.Within, resultado.Outcome);
        Assert.Equal(500_000m, resultado.Ceiling);
    }

    [Fact]
    public async Task RubricaDesconhecida_NaoRecuaParaODepartamento()
    {
        var centro = Centro();
        var store = new FakePlanningStore().With(centro).With(OrcamentoAprovado(centro.Id));

        var resultado = await new BudgetAvailability(store).CheckAsync(
            Pergunta(1m, rubrica: Guid.CreateVersion7().ToString()), CancellationToken.None);

        // Recuar seria verificar contra outro tecto que não o pedido.
        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
    }

    [Fact]
    public async Task RubricaMalFormada_NaoVerifica()
    {
        var resultado = await new BudgetAvailability(new FakePlanningStore())
            .CheckAsync(Pergunta(1m, rubrica: "nao-e-um-guid"), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
    }

    [Fact]
    public async Task CentroDesactivado_NaoVerifica()
    {
        var centro = Centro();
        centro.Deactivate();

        var store = new FakePlanningStore().With(centro).With(OrcamentoAprovado(centro.Id));

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.NoCostCentre, resultado.Outcome);
    }

    /// <summary>
    /// Sem conversão automática, pela mesma razão que a execução de pagamento a
    /// recusa: o câmbio é uma decisão, e ninguém a tomou aqui.
    /// </summary>
    [Fact]
    public async Task MoedaDiferenteDaDoOrcamento_NaoVerifica()
    {
        var centro = Centro();
        var store = new FakePlanningStore()
            .With(centro)
            .With(OrcamentoAprovado(centro.Id, 500_000m, "AOA"));

        var resultado = await new BudgetAvailability(store)
            .CheckAsync(Pergunta(1m, moeda: "USD"), CancellationToken.None);

        Assert.Equal(BudgetCheckOutcome.CurrencyMismatch, resultado.Outcome);
    }

    /// <summary>
    /// O resumo da regra: de cinco resultados possíveis, <strong>um</strong>
    /// deixa passar.
    /// </summary>
    [Fact]
    public void SoUmDosCincoResultadosDeixaPassar()
    {
        var todos = Enum.GetValues<BudgetCheckOutcome>();

        Assert.Equal(5, todos.Length);
        Assert.Single(todos, o => o is BudgetCheckOutcome.Within);
    }
}
