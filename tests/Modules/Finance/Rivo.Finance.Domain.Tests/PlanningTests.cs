using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Planeamento: centro de custo, orçamento e previsão.
///
/// <para>
/// A distinção que estes testes fixam é a de D3 e D4 — <strong>Centro de Custo
/// não é Departamento, e Orçamento não é Previsão de Custos.</strong> O
/// protótipo confundia as duas coisas; o `docs` diz que a divergência é
/// intencional.
/// </para>
/// </summary>
public class PlanningTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static CostCentre Centro(Guid? departamento = null) =>
        CostCentre.Open("CC-OPS", "Operações", departamento, Guid.CreateVersion7());

    private static Budget Orcamento(decimal tectoMensal = 500_000m)
    {
        var orcamento = Budget.Draft(Guid.CreateVersion7(), 2026, "AOA");

        for (var mes = 1; mes <= 12; mes++)
        {
            orcamento.SetMonth(mes, tectoMensal);
        }

        return orcamento;
    }

    // ---- centro de custo ----

    /// <summary>
    /// D4: o mapeamento a Departamento é <strong>opcional</strong>. Nulo é um
    /// estado normal, não dado em falta.
    /// </summary>
    [Fact]
    public void CentroDeCusto_PodeNaoTerDepartamento()
    {
        var centro = Centro();

        Assert.Null(centro.DepartmentId);
        Assert.True(centro.IsActive);
    }

    [Fact]
    public void CentroDeCusto_ExigeResponsavel()
    {
        // Sem responsável não há a quem perguntar por um desvio orçamental.
        Assert.Throws<ArgumentException>(
            () => CostCentre.Open("CC-1", "Operações", null, Guid.Empty));
    }

    [Fact]
    public void CodigoDoCentroENormalizado()
    {
        var centro = CostCentre.Open("  cc-ops  ", "Operações", null, Guid.CreateVersion7());

        Assert.Equal("CC-OPS", centro.Code);
    }

    [Fact]
    public void MapeamentoADepartamentoAlteraSe()
    {
        var centro = Centro();
        var departamento = Guid.CreateVersion7();

        centro.MapToDepartment(departamento);
        Assert.Equal(departamento, centro.DepartmentId);

        centro.MapToDepartment(null);
        Assert.Null(centro.DepartmentId);
    }

    [Fact]
    public void DesactivarNaoElimina()
    {
        var centro = Centro();
        centro.Deactivate();

        Assert.False(centro.IsActive);
        Assert.Equal("CC-OPS", centro.Code);
    }

    // ---- orçamento ----

    [Fact]
    public void OrcamentoNasceEmRascunhoENaoControlaNada()
    {
        var orcamento = Orcamento();

        Assert.Equal(BudgetStatus.Draft, orcamento.Status);

        // **É o que importa para BR-8:** verificar contra números que ninguém
        // aprovou seria dar uma resposta sem valor.
        Assert.False(orcamento.IsInForce);
    }

    [Fact]
    public void TotalAnualEASomaDosMeses()
    {
        Assert.Equal(6_000_000m, Orcamento(500_000m).AnnualTotal);
    }

    [Fact]
    public void FixarOMesmoMesDuasVezes_Revê_NaoAcumula()
    {
        var orcamento = Budget.Draft(Guid.CreateVersion7(), 2026, "AOA");

        orcamento.SetMonth(3, 100_000m);
        orcamento.SetMonth(3, 150_000m);

        Assert.Equal(150_000m, orcamento.CeilingFor(3));
        Assert.Equal(150_000m, orcamento.AnnualTotal);
    }

    [Fact]
    public void MesForaDeUmADoze_ERecusado()
    {
        var orcamento = Budget.Draft(Guid.CreateVersion7(), 2026, "AOA");

        Assert.Throws<ArgumentOutOfRangeException>(() => orcamento.SetMonth(0, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => orcamento.SetMonth(13, 1m));
    }

    [Fact]
    public void TectoNegativo_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Budget.Draft(Guid.CreateVersion7(), 2026, "AOA").SetMonth(1, -1m));
    }

    [Fact]
    public void AprovarPoeEmVigor()
    {
        var orcamento = Orcamento();
        var quem = Guid.CreateVersion7();

        orcamento.Approve(quem, Agora);

        Assert.Equal(BudgetStatus.Approved, orcamento.Status);
        Assert.True(orcamento.IsInForce);
        Assert.Equal(quem, orcamento.ApprovedByEmployeeId);
    }

    /// <summary>
    /// <strong>Se o tecto se pudesse subir depois de aprovado, BR-8 não
    /// verificaria nada</strong> — bastaria subi-lo para o próprio pedido
    /// passar a caber.
    /// </summary>
    [Fact]
    public void OrcamentoAprovado_NaoSeAltera()
    {
        var orcamento = Orcamento();
        orcamento.Approve(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(() => orcamento.SetMonth(1, 9_000_000m));
    }

    [Fact]
    public void OrcamentoSemMeses_NaoSeAprova()
    {
        var vazio = Budget.Draft(Guid.CreateVersion7(), 2026, "AOA");

        Assert.Throws<InvalidOperationException>(() => vazio.Approve(Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void AprovarDuasVezes_ERecusado()
    {
        var orcamento = Orcamento();
        orcamento.Approve(Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(() => orcamento.Approve(Guid.CreateVersion7(), Agora));
    }

    /// <summary>
    /// Fechado deixa de controlar sem passar a rascunho: os números ficam
    /// legíveis, que é o que BR-14 quer dizer aqui.
    /// </summary>
    [Fact]
    public void FecharDeixaDeControlarMasNaoApaga()
    {
        var orcamento = Orcamento();
        orcamento.Approve(Guid.CreateVersion7(), Agora);
        orcamento.Close();

        Assert.Equal(BudgetStatus.Closed, orcamento.Status);
        Assert.False(orcamento.IsInForce);
        Assert.Equal(6_000_000m, orcamento.AnnualTotal);
    }

    [Fact]
    public void RascunhoNaoSeFecha()
    {
        Assert.Throws<InvalidOperationException>(() => Orcamento().Close());
    }

    [Fact]
    public void MesSemTecto_NaoTemLimite_ENaoZero()
    {
        var orcamento = Budget.Draft(Guid.CreateVersion7(), 2026, "AOA");
        orcamento.SetMonth(1, 100_000m);

        // Nulo e não zero: "não orçamentado" é diferente de "orçamentado a
        // zero", e BR-8 trata os dois de forma distinta.
        Assert.Null(orcamento.CeilingFor(2));
        Assert.Equal(100_000m, orcamento.CeilingFor(1));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDoOrcamento()
    {
        var orcamento = Orcamento();
        orcamento.Approve(Guid.CreateVersion7(), Agora);

        Assert.Equal(0, orcamento.Version);
    }

    // ---- previsão de custos (D3) ----

    /// <summary>
    /// D3: a previsão é do <strong>departamento</strong>, o orçamento é do
    /// <strong>centro de custo</strong>. Coexistem sobre o mesmo período sem se
    /// fundirem.
    /// </summary>
    [Fact]
    public void PrevisaoEDoDepartamentoENaoDoCentroDeCusto()
    {
        var departamento = Guid.CreateVersion7();
        var previsao = DepartmentCostForecast.Draft(departamento, 2026, 8, "AOA", 300_000m, 200_000m);

        Assert.Equal(departamento, previsao.DepartmentId);
        Assert.Equal(500_000m, previsao.Total);
    }

    /// <summary>
    /// Separa operacionais de fixos porque é essa a repartição que o
    /// carregamento de caixa usa: os fixos saem sempre, os operacionais variam.
    /// </summary>
    [Fact]
    public void CustosOperacionaisEFixosSaoDistintos()
    {
        var previsao = DepartmentCostForecast.Draft(
            Guid.CreateVersion7(), 2026, 8, "AOA", 300_000m, 200_000m);

        Assert.Equal(300_000m, previsao.OperationalCosts);
        Assert.Equal(200_000m, previsao.FixedCosts);
    }

    [Fact]
    public void PrevisaoSubmetida_NaoSeAltera()
    {
        var previsao = DepartmentCostForecast.Draft(
            Guid.CreateVersion7(), 2026, 8, "AOA", 300_000m, 200_000m);

        previsao.Submit(Agora);

        Assert.Equal(ForecastStatus.Submitted, previsao.Status);

        // O carregamento de caixa já a leu.
        Assert.Throws<InvalidOperationException>(() => previsao.Revise(1m, 1m));
    }

    [Fact]
    public void SubmeterDuasVezes_ERecusado()
    {
        var previsao = DepartmentCostForecast.Draft(
            Guid.CreateVersion7(), 2026, 8, "AOA", 1m, 1m);

        previsao.Submit(Agora);

        Assert.Throws<InvalidOperationException>(() => previsao.Submit(Agora));
    }

    [Fact]
    public void PrevisaoNegativa_ERecusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DepartmentCostForecast.Draft(Guid.CreateVersion7(), 2026, 8, "AOA", -1m, 0m));
    }

    [Fact]
    public void MoedaForaDoISO4217_ERecusada()
    {
        Assert.Throws<ArgumentException>(
            () => DepartmentCostForecast.Draft(Guid.CreateVersion7(), 2026, 8, "KWANZA", 1m, 1m));

        Assert.Throws<ArgumentException>(
            () => Budget.Draft(Guid.CreateVersion7(), 2026, "KWANZA"));
    }
}
