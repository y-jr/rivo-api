namespace Rivo.Payroll.Domain;

/// <summary>
/// Folha de Pagamento. Ver `modules/payroll.md`.
///
/// <para>
/// <strong>Cálculo fiscal via `fiscal`.</strong> A ordem de cálculo do IRT
/// está confirmada em lei (artigo 7.º do Código do IRT); os escalões
/// concretos e as taxas de INSS vêm de `fiscal`, único módulo autorizado a
/// implementar regra fiscal (`modules/fiscal.md`). `payroll` nunca calcula
/// o imposto — pergunta a `fiscal` à data do facto gerador e aplica o
/// resultado via <see cref="PayrollItem.ApplyCalculation"/>.
/// </para>
///
/// <para>
/// <strong>Aprovação, sim — governança não depende do cálculo estar feito.</strong>
/// A folha segue o mesmo padrão de `procurement.PurchaseRequisition`: submete-se
/// a `approval` pelo total bruto, e só aprovada fica pronta. `approval` nunca
/// altera dados de negócio — o efeito é aplicado deste lado, por
/// <see cref="MarkApproved"/>/<see cref="MarkRefused"/>, quando `payroll`
/// pergunta o estado.
/// </para>
/// </summary>
public sealed class PayrollRun
{
    private readonly List<PayrollItem> _items = [];

    private PayrollRun(Guid id, int year, int month, Guid openedByEmployeeId)
    {
        Id = id;
        Year = year;
        Month = month;
        OpenedByEmployeeId = openedByEmployeeId;
        Status = PayrollRunStatus.Draft;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PayrollRun()
    {
    }

    public Guid Id { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    /// <summary>Quem abriu a folha — usado para a submeter (é o requerente que `approval` espera).</summary>
    public Guid OpenedByEmployeeId { get; private set; }

    public PayrollRunStatus Status { get; private set; }

    /// <summary>
    /// O pedido em `approval` que decide esta folha. Nulo até à submissão —
    /// mesmo desenho de `PurchaseRequisition.ApprovalRequestId`.
    /// </summary>
    public Guid? ApprovalRequestId { get; private set; }

    public IReadOnlyList<PayrollItem> Items => _items;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static PayrollRun Open(int year, int month, Guid openedByEmployeeId)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "O mês tem de estar entre 1 e 12.");
        }

        return new PayrollRun(Guid.CreateVersion7(), year, month, openedByEmployeeId);
    }

    /// <summary>
    /// Acrescenta um item, com o bruto e a composição de subsídios que têm
    /// tratamento próprio no IRT — ver `PayrollItem` para a validação e
    /// `modules/payroll.md` para o porquê de cada um.
    /// </summary>
    public PayrollItem AddItem(
        Guid employeeId,
        decimal grossSalary,
        decimal foodAllowance = 0m,
        decimal transportAllowance = 0m,
        decimal vacationAllowance = 0m,
        decimal christmasAllowance = 0m)
    {
        if (Status is not PayrollRunStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Só se acrescentam itens a uma folha em rascunho. Esta está em {Status}.");
        }

        var item = PayrollItem.Register(
            Id, employeeId, grossSalary, foodAllowance, transportAllowance, vacationAllowance, christmasAllowance);
        _items.Add(item);

        return item;
    }

    /// <summary>Total bruto do período — o único número que a folha tem para submeter.</summary>
    public decimal TotalGross => _items.Sum(i => i.GrossSalary);

    /// <summary>
    /// O facto gerador do imposto deste período — o último dia do mês a que a
    /// folha respeita. É a data que se passa a `fiscal` para determinar o
    /// INSS e o IRT em vigor (ADR-011 §3: determinação à data do facto
    /// gerador, nunca a data corrente).
    /// </summary>
    public DateOnly PeriodEndDate => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>
    /// Marca a folha como submetida. Chamado depois de `approval` aceitar —
    /// o composition root é quem fala com `approval`; este método só regista
    /// o resultado (mesmo desenho de `PurchaseRequisition.MarkSubmitted`).
    /// </summary>
    public void MarkSubmitted(Guid approvalRequestId, DateTimeOffset at)
    {
        if (Status is not PayrollRunStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Só um rascunho se submete. Esta folha está em {Status}.");
        }

        if (_items.Count == 0)
        {
            throw new InvalidOperationException("Uma folha sem itens não tem o que aprovar.");
        }

        Status = PayrollRunStatus.PendingApproval;
        ApprovalRequestId = approvalRequestId;
        SubmittedAt = at;
    }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public void MarkApproved(DateTimeOffset at)
    {
        if (Status is not PayrollRunStatus.PendingApproval)
        {
            throw new InvalidOperationException("Esta folha não está à espera de decisão.");
        }

        Status = PayrollRunStatus.Approved;
        ClosedAt = at;
    }

    public void MarkRefused(DateTimeOffset at)
    {
        if (Status is not PayrollRunStatus.PendingApproval)
        {
            throw new InvalidOperationException("Esta folha não está à espera de decisão.");
        }

        Status = PayrollRunStatus.Refused;
        ClosedAt = at;
    }
}

public enum PayrollRunStatus
{
    Draft,
    PendingApproval,
    Approved,
    Refused,
}

/// <summary>
/// Item de folha, por colaborador. Nasce com o bruto e a composição de
/// subsídios; o cálculo fiscal (INSS e IRT) é aplicado depois, via
/// <see cref="ApplyCalculation"/>, pelo caso de uso que pergunta a `fiscal`
/// — ver o comentário em <see cref="PayrollRun"/>.
///
/// <para>
/// <strong>Bruto é o total, os subsídios são componentes dele</strong> —
/// não se somam ao bruto, descrevem-no. Um item com bruto 350.000 e
/// alimentação 30.000 não recebe 380.000: recebe 350.000, dos quais 30.000
/// são a título de subsídio de alimentação. É essa leitura que faz
/// <c>Sum(subsídios) ≤ GrossSalary</c> ser a invariante certa.
/// </para>
///
/// <para>
/// <strong>Só Alimentação e Transporte têm tratamento fiscal próprio.</strong>
/// Férias e Natal são tributados normalmente — confirmado pelo utilizador,
/// não inventado — por isso ficam registados aqui (para o recibo os
/// mostrar), mas não entram em nenhum cálculo de isenção; ver
/// `AddPayrollItem` (Application) para onde a isenção de facto se aplica.
/// </para>
/// </summary>
public sealed class PayrollItem
{
    private PayrollItem(
        Guid id,
        Guid runId,
        Guid employeeId,
        decimal grossSalary,
        decimal foodAllowance,
        decimal transportAllowance,
        decimal vacationAllowance,
        decimal christmasAllowance)
    {
        Id = id;
        RunId = runId;
        EmployeeId = employeeId;
        GrossSalary = grossSalary;
        FoodAllowance = foodAllowance;
        TransportAllowance = transportAllowance;
        VacationAllowance = vacationAllowance;
        ChristmasAllowance = christmasAllowance;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PayrollItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public decimal GrossSalary { get; private set; }

    /// <summary>Subsídio de alimentação — isento até um limiar (`fiscal`), o resto tributável.</summary>
    public decimal FoodAllowance { get; private set; }

    /// <summary>Subsídio de transporte — isento até um limiar (`fiscal`), o resto tributável.</summary>
    public decimal TransportAllowance { get; private set; }

    /// <summary>Subsídio de férias — tributado normalmente, sem isenção. Só composição do recibo.</summary>
    public decimal VacationAllowance { get; private set; }

    /// <summary>Subsídio de Natal — tributado normalmente, sem isenção. Só composição do recibo.</summary>
    public decimal ChristmasAllowance { get; private set; }

    /// <summary>Nulo até <see cref="ApplyCalculation"/>.</summary>
    public decimal? NetSalary { get; private set; }

    /// <summary>O IRT retido — nulo até <see cref="ApplyCalculation"/>.</summary>
    public decimal? WithholdingTax { get; private set; }

    /// <summary>A contribuição de INSS a cargo do trabalhador — nulo até <see cref="ApplyCalculation"/>.</summary>
    public decimal? SocialSecurityContribution { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal static PayrollItem Register(
        Guid runId,
        Guid employeeId,
        decimal grossSalary,
        decimal foodAllowance,
        decimal transportAllowance,
        decimal vacationAllowance,
        decimal christmasAllowance)
    {
        if (grossSalary <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossSalary), "O salário bruto tem de ser positivo.");
        }

        foreach (var (nome, valor) in new[]
                 {
                     (nameof(foodAllowance), foodAllowance),
                     (nameof(transportAllowance), transportAllowance),
                     (nameof(vacationAllowance), vacationAllowance),
                     (nameof(christmasAllowance), christmasAllowance),
                 })
        {
            if (valor < 0)
            {
                throw new ArgumentOutOfRangeException(nome, valor, "Um subsídio não pode ser negativo.");
            }
        }

        var somaDosSubsidios = foodAllowance + transportAllowance + vacationAllowance + christmasAllowance;

        if (somaDosSubsidios > grossSalary)
        {
            throw new ArgumentException(
                $"Os subsídios ({somaDosSubsidios}) não cabem no bruto ({grossSalary}) — são componentes " +
                "dele, não uma soma à parte.",
                nameof(grossSalary));
        }

        return new PayrollItem(
            Guid.CreateVersion7(), runId, employeeId, grossSalary,
            foodAllowance, transportAllowance, vacationAllowance, christmasAllowance);
    }

    /// <summary>
    /// Aplica o resultado do cálculo fiscal, já determinado por `fiscal`.
    ///
    /// <para>
    /// <strong>O líquido calcula-se aqui, não se recebe.</strong> Um terceiro
    /// parâmetro <c>netSalary</c> seria redundante com <c>bruto − IRT − INSS</c>
    /// e permitiria os dois discordarem — a invariante fica verdadeira por
    /// construção só se for este método a fazer a subtracção.
    /// </para>
    /// </summary>
    public void ApplyCalculation(decimal withholdingTax, decimal socialSecurityContribution)
    {
        if (withholdingTax < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(withholdingTax), "O IRT retido não pode ser negativo.");
        }

        if (socialSecurityContribution < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(socialSecurityContribution), "A contribuição de INSS não pode ser negativa.");
        }

        WithholdingTax = withholdingTax;
        SocialSecurityContribution = socialSecurityContribution;
        NetSalary = GrossSalary - withholdingTax - socialSecurityContribution;
    }
}
