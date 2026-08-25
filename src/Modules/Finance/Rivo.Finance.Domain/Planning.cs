namespace Rivo.Finance.Domain;

/// <summary>
/// Centro de custo.
///
/// <para>
/// <strong>Distinto de Departamento, e o mapeamento não é obrigatório</strong>
/// (D4, ADR-005). O protótipo confundia os dois; o `docs` fixou que a
/// divergência é intencional — nem todo o centro de custo corresponde a um
/// departamento, e um departamento pode alimentar vários. `hr` possui o
/// Departamento; aqui guarda-se um ponteiro opcional para ele.
/// </para>
///
/// <para>
/// O responsável é um Colaborador, e <strong>pode não ser o gestor do
/// departamento</strong> — é precisamente essa divergência que o `docs` manda
/// preservar.
/// </para>
/// </summary>
public sealed class CostCentre
{
    private CostCentre(string code, string name, Guid? departmentId, Guid responsibleEmployeeId)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        DepartmentId = departmentId;
        ResponsibleEmployeeId = responsibleEmployeeId;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private CostCentre()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Departamento de `hr`, <strong>opcional por desenho</strong> (D4). Nulo
    /// não é dado em falta.
    /// </summary>
    public Guid? DepartmentId { get; private set; }

    public Guid ResponsibleEmployeeId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    public static CostCentre Open(
        string code, string name, Guid? departmentId, Guid responsibleEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Um centro de custo precisa de código.", nameof(code));
        }

        if (code.Trim().Length > 20)
        {
            throw new ArgumentException("O código vai até 20 caracteres.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um centro de custo precisa de nome.", nameof(name));
        }

        if (responsibleEmployeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um centro de custo tem responsável — sem ele não há a quem perguntar " +
                "por um desvio orçamental.",
                nameof(responsibleEmployeeId));
        }

        return new CostCentre(
            code.Trim().ToUpperInvariant(), name.Trim(), departmentId, responsibleEmployeeId);
    }

    public void Reassign(Guid responsibleEmployeeId)
    {
        if (responsibleEmployeeId == Guid.Empty)
        {
            throw new ArgumentException("Um centro de custo tem responsável.", nameof(responsibleEmployeeId));
        }

        ResponsibleEmployeeId = responsibleEmployeeId;
    }

    public void MapToDepartment(Guid? departmentId) => DepartmentId = departmentId;

    /// <summary>
    /// Desactiva. Não elimina (BR-14): os orçamentos e as linhas de lançamento
    /// que lhe apontam continuam a fazer sentido.
    /// </summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Orçamento anual de um centro de custo, com uma linha por mês.
///
/// <para>
/// <strong>É um tecto de controlo</strong> — e é isso que o distingue da
/// Previsão de Custos Departamentais (D3): o orçamento diz quanto se
/// <em>pode</em> gastar, a previsão diz quanto se <em>espera</em> gastar para
/// efeitos de carregamento de caixa. São entidades distintas e
/// <strong>nunca se fundem</strong>, mesmo quando falam do mesmo período.
/// </para>
///
/// <para>
/// É contra este tecto que BR-8 verifica, e por isso ele tem estado: um
/// orçamento em rascunho não controla nada, e verificar contra números que
/// ninguém aprovou seria dar a BR-8 uma resposta sem valor.
/// </para>
/// </summary>
public sealed class Budget
{
    private readonly List<BudgetLine> _lines = [];

    private Budget(Guid costCentreId, int fiscalYear, string currency)
    {
        Id = Guid.CreateVersion7();
        CostCentreId = costCentreId;
        FiscalYear = fiscalYear;
        Currency = currency;
        Status = BudgetStatus.Draft;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Budget()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CostCentreId { get; private set; }

    public int FiscalYear { get; private set; }

    /// <summary>ISO 4217. Um orçamento tem uma moeda só.</summary>
    public string Currency { get; private set; }

    public BudgetStatus Status { get; private set; }

    public IReadOnlyList<BudgetLine> Lines => _lines;

    public decimal AnnualTotal { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedByEmployeeId { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    public static Budget Draft(Guid costCentreId, int fiscalYear, string currency)
    {
        if (costCentreId == Guid.Empty)
        {
            throw new ArgumentException("Um orçamento é de um centro de custo.", nameof(costCentreId));
        }

        if (fiscalYear is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "Ano fiscal fora do intervalo.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217.", nameof(currency));
        }

        return new Budget(costCentreId, fiscalYear, currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Fixa o tecto de um mês. Só em rascunho — depois de aprovado, alterar o
    /// tecto sem que ninguém volte a aprovar esvaziaria a aprovação.
    /// </summary>
    public void SetMonth(int month, decimal amount)
    {
        if (Status is not BudgetStatus.Draft)
        {
            throw new InvalidOperationException(
                "Um orçamento aprovado não se altera. Reveja-o — e essa revisão volta a ser aprovada.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "O mês vai de 1 a 12.");
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Um tecto orçamental não é negativo.");
        }

        var existente = _lines.FirstOrDefault(l => l.Month == month);

        if (existente is null)
        {
            _lines.Add(BudgetLine.For(Id, month, decimal.Round(amount, 2, MidpointRounding.AwayFromZero)));
        }
        else
        {
            existente.Revise(decimal.Round(amount, 2, MidpointRounding.AwayFromZero));
        }

        AnnualTotal = _lines.Sum(l => l.Amount);
    }

    /// <summary>
    /// Aprova o orçamento. A partir daqui controla — e deixa de se mexer.
    /// </summary>
    public void Approve(Guid approvedByEmployeeId, DateTimeOffset at)
    {
        if (Status is not BudgetStatus.Draft)
        {
            throw new InvalidOperationException("Só um rascunho se aprova.");
        }

        if (approvedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException("A aprovação regista quem aprovou.", nameof(approvedByEmployeeId));
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Um orçamento sem meses nenhuns não é um tecto — é um documento vazio.");
        }

        Status = BudgetStatus.Approved;
        ApprovedAt = at;
        ApprovedByEmployeeId = approvedByEmployeeId;
    }

    /// <summary>
    /// Fecha o orçamento — o ano acabou. Deixa de controlar sem passar a
    /// rascunho: os números ficam legíveis, e é isso que BR-14 quer dizer aqui.
    /// </summary>
    public void Close()
    {
        if (Status is not BudgetStatus.Approved)
        {
            throw new InvalidOperationException("Só um orçamento aprovado se fecha.");
        }

        Status = BudgetStatus.Closed;
    }

    /// <summary>O tecto de um mês, ou nulo se esse mês não foi orçamentado.</summary>
    public decimal? CeilingFor(int month) =>
        _lines.FirstOrDefault(l => l.Month == month)?.Amount;

    /// <summary>Verdadeiro só enquanto o orçamento controla de facto.</summary>
    public bool IsInForce => Status is BudgetStatus.Approved;
}

public sealed class BudgetLine
{
    private BudgetLine(Guid budgetId, int month, decimal amount)
    {
        Id = Guid.CreateVersion7();
        BudgetId = budgetId;
        Month = month;
        Amount = amount;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private BudgetLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid BudgetId { get; private set; }

    public int Month { get; private set; }

    public decimal Amount { get; private set; }

    internal static BudgetLine For(Guid budgetId, int month, decimal amount) =>
        new(budgetId, month, amount);

    internal void Revise(decimal amount) => Amount = amount;
}

public enum BudgetStatus
{
    /// <summary>Em elaboração. **Não controla nada** — BR-8 ignora-o.</summary>
    Draft,

    /// <summary>Em vigor. É contra este que BR-8 verifica.</summary>
    Approved,

    /// <summary>O ano acabou. Fica legível, deixa de controlar.</summary>
    Closed,
}

/// <summary>
/// Previsão de custos de um departamento para um mês.
///
/// <para>
/// <strong>Entidade distinta do Orçamento, e a distinção é vinculativa</strong>
/// (D3). O orçamento é do centro de custo e é um tecto; a previsão é do
/// departamento e é um input ao carregamento de caixa. Fundi-los faria um
/// número de tesouraria passar a controlar despesa, ou um tecto de controlo
/// passar a prever saídas — e nenhuma das duas coisas é verdade.
/// </para>
///
/// <para>
/// Separa custos operacionais de fixos porque é essa a repartição que o
/// carregamento de caixa usa: os fixos saem sempre, os operacionais variam.
/// </para>
/// </summary>
public sealed class DepartmentCostForecast
{
    private DepartmentCostForecast(
        Guid departmentId, int fiscalYear, int month, string currency)
    {
        Id = Guid.CreateVersion7();
        DepartmentId = departmentId;
        FiscalYear = fiscalYear;
        Month = month;
        Currency = currency;
        Status = ForecastStatus.Draft;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private DepartmentCostForecast()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Departamento de `hr` — <strong>não</strong> centro de custo.</summary>
    public Guid DepartmentId { get; private set; }

    public int FiscalYear { get; private set; }

    public int Month { get; private set; }

    public string Currency { get; private set; }

    public decimal OperationalCosts { get; private set; }

    public decimal FixedCosts { get; private set; }

    public decimal Total => OperationalCosts + FixedCosts;

    public ForecastStatus Status { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    public static DepartmentCostForecast Draft(
        Guid departmentId,
        int fiscalYear,
        int month,
        string currency,
        decimal operationalCosts,
        decimal fixedCosts)
    {
        if (departmentId == Guid.Empty)
        {
            throw new ArgumentException("A previsão é de um departamento.", nameof(departmentId));
        }

        if (fiscalYear is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "Ano fora do intervalo.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "O mês vai de 1 a 12.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217.", nameof(currency));
        }

        var previsao = new DepartmentCostForecast(
            departmentId, fiscalYear, month, currency.Trim().ToUpperInvariant());

        previsao.Revise(operationalCosts, fixedCosts);

        return previsao;
    }

    public void Revise(decimal operationalCosts, decimal fixedCosts)
    {
        if (Status is not ForecastStatus.Draft)
        {
            throw new InvalidOperationException(
                "Uma previsão submetida não se altera — o carregamento de caixa já a leu.");
        }

        if (operationalCosts < 0 || fixedCosts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationalCosts), "Uma previsão de custo não é negativa.");
        }

        OperationalCosts = decimal.Round(operationalCosts, 2, MidpointRounding.AwayFromZero);
        FixedCosts = decimal.Round(fixedCosts, 2, MidpointRounding.AwayFromZero);
    }

    public void Submit(DateTimeOffset at)
    {
        if (Status is not ForecastStatus.Draft)
        {
            throw new InvalidOperationException("A previsão já foi submetida.");
        }

        Status = ForecastStatus.Submitted;
        SubmittedAt = at;
    }
}

public enum ForecastStatus
{
    Draft,
    Submitted,
}
