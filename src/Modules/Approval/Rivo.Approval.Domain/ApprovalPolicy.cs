namespace Rivo.Approval.Domain;

/// <summary>
/// Política de Aprovação — a configuração que diz quem aprova o quê.
///
/// <para>
/// <strong>É configurável; o workflow não é.</strong> `modules/approval.md`
/// fixa a distinção: alçadas, cargos, departamentos e faixas de valor são
/// configuração da organização, mas as regras de domínio — BR-2, BR-4,
/// decisões imutáveis — não podem ser desactivadas nem enfraquecidas por
/// configuração administrativa.
/// </para>
///
/// <para>
/// Referencia Cargos de `hr` <strong>por identificador</strong>, nunca
/// duplicando o catálogo (ADR-010).
/// </para>
/// </summary>
public sealed class ApprovalPolicy
{
    private readonly List<PolicyStep> _steps = [];

    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private ApprovalPolicy() => ProcessType = string.Empty;

    private ApprovalPolicy(
        Guid id,
        string processType,
        Guid? departmentId,
        decimal? minimumAmount,
        decimal? maximumAmount,
        bool requiresBudgetCheck)
    {
        Id = id;
        ProcessType = processType;
        DepartmentId = departmentId;
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        RequiresBudgetCheck = requiresBudgetCheck;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public string ProcessType { get; private set; }

    /// <summary>Nulo aplica-se a todos os departamentos.</summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>Mínimo inclusivo. Nulo, sem limite inferior.</summary>
    public decimal? MinimumAmount { get; private set; }

    /// <summary>Máximo <strong>exclusivo</strong>. Nulo, sem limite superior.</summary>
    public decimal? MaximumAmount { get; private set; }

    /// <summary>
    /// Exige verificação orçamental antes da decisão (BR-8).
    ///
    /// <para>
    /// Enquanto `finance` não existir, uma política com esta marca recusa
    /// submissões — em vez de as deixar passar afirmando ter verificado o que
    /// não verificou (ADR-034).
    /// </para>
    /// </summary>
    public bool RequiresBudgetCheck { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<PolicyStep> Steps => _steps;

    public static ApprovalPolicy Create(
        string processType,
        Guid? departmentId = null,
        decimal? minimumAmount = null,
        decimal? maximumAmount = null,
        bool requiresBudgetCheck = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);

        if (minimumAmount is < 0)
        {
            throw new ArgumentException("O mínimo da faixa não pode ser negativo.", nameof(minimumAmount));
        }

        if (minimumAmount is { } min && maximumAmount is { } max && max <= min)
        {
            throw new ArgumentException(
                "O máximo da faixa tem de ser superior ao mínimo.",
                nameof(maximumAmount));
        }

        return new ApprovalPolicy(
            Guid.CreateVersion7(),
            processType.Trim(),
            departmentId,
            minimumAmount,
            maximumAmount,
            requiresBudgetCheck);
    }

    /// <summary>
    /// Acrescenta um passo. A ordem é atribuída pela sequência de inserção — um
    /// passo é sempre o seguinte do anterior, sem números à escolha que possam
    /// colidir ou deixar buracos.
    /// </summary>
    public PolicyStep AddStep(Guid approverPositionId, StepMode mode = StepMode.AnyApprover, int? slaHours = null)
    {
        if (approverPositionId == Guid.Empty)
        {
            throw new ArgumentException("Um passo aprova por Cargo.", nameof(approverPositionId));
        }

        if (slaHours is <= 0)
        {
            throw new ArgumentException("O prazo, se definido, é positivo.", nameof(slaHours));
        }

        var step = PolicyStep.Create(Id, _steps.Count + 1, approverPositionId, mode, slaHours);
        _steps.Add(step);

        return step;
    }

    /// <summary>
    /// Corresponde a este processo?
    ///
    /// <para>
    /// O valor é comparado com a faixa por <c>[mínimo, máximo[</c> — máximo
    /// exclusivo, para que faixas contíguas não se sobreponham. Com
    /// <c>0–1000</c> e <c>1000–5000</c>, o valor 1000 cai numa só.
    /// </para>
    /// </summary>
    public bool Matches(string processType, Guid? departmentId, decimal? amount)
    {
        if (!IsActive || !string.Equals(ProcessType, processType, StringComparison.Ordinal))
        {
            return false;
        }

        // Política de departamento só serve o seu; política sem departamento
        // serve todos.
        if (DepartmentId is not null && DepartmentId != departmentId)
        {
            return false;
        }

        if (MinimumAmount is null && MaximumAmount is null)
        {
            return true;
        }

        // A política tem faixa e o pedido não traz valor: não corresponde. Um
        // processo sem valor não pode cair numa alçada por omissão.
        if (amount is not { } valor)
        {
            return false;
        }

        return (MinimumAmount is null || valor >= MinimumAmount)
            && (MaximumAmount is null || valor < MaximumAmount);
    }

    /// <summary>
    /// Quão específica é, para desempatar entre políticas que correspondem.
    ///
    /// <para>
    /// Departamento definido vale mais do que departamento nulo; faixa definida
    /// vale mais do que faixa aberta. Duas políticas com a mesma
    /// especificidade são <strong>empate</strong>, e o empate recusa-se em vez
    /// de se escolher uma — significaria que ninguém sabe qual é a alçada
    /// (ADR-034).
    /// </para>
    /// </summary>
    public int Specificity =>
        (DepartmentId is not null ? 2 : 0)
        + (MinimumAmount is not null || MaximumAmount is not null ? 1 : 0);

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// Passo de uma política: quem aprova, em que ordem, e em que modo.
/// </summary>
public sealed class PolicyStep
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private PolicyStep() { }

    private PolicyStep(Guid id, Guid policyId, int order, Guid approverPositionId, StepMode mode, int? slaHours)
    {
        Id = id;
        PolicyId = policyId;
        Order = order;
        ApproverPositionId = approverPositionId;
        Mode = mode;
        SlaHours = slaHours;
    }

    public Guid Id { get; private set; }

    public Guid PolicyId { get; private set; }

    /// <summary>Começa em 1. Passos sequenciais abrem por esta ordem.</summary>
    public int Order { get; private set; }

    /// <summary>Cargo de `hr` cujos ocupantes aprovam este passo (ADR-010).</summary>
    public Guid ApproverPositionId { get; private set; }

    public StepMode Mode { get; private set; }

    /// <summary>
    /// Prazo em horas.
    ///
    /// <para>
    /// <strong>É registado e nada o faz cumprir.</strong> Escalonamento
    /// automático exige decidir o que acontece ao fim do prazo — avançar,
    /// notificar ou reatribuir — e essa decisão está em aberto
    /// (`modules/approval.md`, ADR-034).
    /// </para>
    /// </summary>
    public int? SlaHours { get; private set; }

    internal static PolicyStep Create(Guid policyId, int order, Guid approverPositionId, StepMode mode, int? slaHours) =>
        new(Guid.CreateVersion7(), policyId, order, approverPositionId, mode, slaHours);
}

/// <summary>
/// Quantos dos aprovadores resolvidos para um passo têm de decidir.
///
/// <para>
/// <strong>O modo é sobre pessoas dentro do passo, não sobre a ordem dos
/// passos</strong> — os passos correm sempre por ordem. Um passo aponta para
/// um <em>Cargo</em>, e um Cargo pode ter mais do que um ocupante.
/// </para>
/// </summary>
public enum StepMode
{
    /// <summary>
    /// Basta um dos ocupantes do Cargo (omissão).
    ///
    /// <para>
    /// Quem ocupa um Cargo <strong>representa esse Cargo</strong>: dois
    /// directores financeiros são intermutáveis para efeitos de decidir em nome
    /// da direcção financeira. Exigir os dois travaria o processo sempre que um
    /// estivesse de férias.
    /// </para>
    /// </summary>
    AnyApprover,

    /// <summary>
    /// Todos os ocupantes têm de decidir.
    ///
    /// <para>
    /// Para passos em que a assinatura conjunta é o ponto — duas chaves para o
    /// mesmo cofre. Escolha deliberada de quem configura a política, e não o
    /// comportamento por omissão.
    /// </para>
    /// </summary>
    AllApprovers,
}
