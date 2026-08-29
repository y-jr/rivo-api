namespace Rivo.Payroll.Domain;

/// <summary>
/// Folha de Pagamento. Esqueleto do módulo — ver `modules/payroll.md`.
///
/// <para>
/// <strong>Fatia mínima, deliberada, sem cálculo fiscal.</strong> A ordem de
/// cálculo do IRT está confirmada em lei (artigo 7.º do Código do IRT), mas os
/// escalões concretos vêm de `fiscal`, que não tem tabela angolana carregada
/// — e `CLAUDE.md` proíbe implementar regras fiscais a partir de levantamento
/// não verificado. Por isso <see cref="PayrollItem"/> só tem o salário bruto:
/// um número calculado sem regra real por trás mentiria pior do que a
/// ausência do campo.
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
    /// Acrescenta um item. **Só o salário bruto** — ver o porquê no comentário
    /// da classe.
    /// </summary>
    public PayrollItem AddItem(Guid employeeId, decimal grossSalary)
    {
        if (Status is not PayrollRunStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Só se acrescentam itens a uma folha em rascunho. Esta está em {Status}.");
        }

        var item = PayrollItem.Register(Id, employeeId, grossSalary);
        _items.Add(item);

        return item;
    }

    /// <summary>Total bruto do período — o único número que a folha tem para submeter.</summary>
    public decimal TotalGross => _items.Sum(i => i.GrossSalary);

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
/// Item de folha, por colaborador. **Só o bruto** — ver o comentário em
/// <see cref="PayrollRun"/>. Os campos de cálculo existem no modelo de dados,
/// porque o desenho da folha os prevê, mas ficam sempre nulos: não há motor
/// de cálculo por trás deles ainda.
/// </summary>
public sealed class PayrollItem
{
    private PayrollItem(Guid id, Guid runId, Guid employeeId, decimal grossSalary)
    {
        Id = id;
        RunId = runId;
        EmployeeId = employeeId;
        GrossSalary = grossSalary;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PayrollItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public decimal GrossSalary { get; private set; }

    /// <summary>
    /// Nulo sempre, por agora. Sem tabela de IRT carregada em `fiscal`, não há
    /// como calcular sem inventar — e inventar seria pior do que não calcular.
    /// </summary>
    public decimal? NetSalary { get; private set; }

    /// <summary>Nulo sempre, por agora. Ver <see cref="NetSalary"/>.</summary>
    public decimal? WithholdingTax { get; private set; }

    /// <summary>Nulo sempre, por agora. Ver <see cref="NetSalary"/>.</summary>
    public decimal? SocialSecurityContribution { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal static PayrollItem Register(Guid runId, Guid employeeId, decimal grossSalary)
    {
        if (grossSalary <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossSalary), "O salário bruto tem de ser positivo.");
        }

        return new PayrollItem(Guid.CreateVersion7(), runId, employeeId, grossSalary);
    }
}
