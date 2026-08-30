namespace Rivo.Projects.Domain;

/// <summary>
/// Orçamento de um projecto — parte do agregado <see cref="Project"/>
/// (`modules/projects.md` §Possui).
///
/// <para>
/// <strong>Distinto do orçamento por centro de custo de `finance`</strong>
/// (ADR-040, ADR-037) — este é a dotação do próprio projecto, não o tecto
/// financeiro da organização. As duas entidades **nunca se fundem**, mesmo
/// quando o mesmo projecto tem as duas. Validar uma despesa de projecto
/// contra o disponível de `finance` sem duplicar a entidade é trabalho por
/// desenhar — ADR-040 fixa que a relação existe, não como se concretiza.
/// </para>
///
/// <para>
/// <strong>Zero ou um por projecto</strong>, ao contrário de Marco e Tarefa —
/// um projecto não tem "vários orçamentos", tem um, revisto ao longo do
/// tempo. <see cref="Project.SetBudget"/> cria-o da primeira vez e revê-o
/// depois, sem histórico de revisões — só o valor actual.
/// </para>
/// </summary>
public sealed class ProjectBudget
{
    internal ProjectBudget(Guid id, Guid projectId, decimal amount, string currency, DateTimeOffset setAt)
    {
        Id = id;
        ProjectId = projectId;
        Amount = amount;
        Currency = currency;
        SetAt = setAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private ProjectBudget()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>ISO 4217. Fixada na primeira vez — uma revisão não muda a moeda.</summary>
    public string Currency { get; private set; }

    /// <summary>Quando o valor actual foi fixado — a última definição ou revisão.</summary>
    public DateTimeOffset SetAt { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal void Revise(decimal amount, DateTimeOffset at)
    {
        Amount = amount;
        SetAt = at;
    }
}
