namespace Rivo.Projects.Domain;

/// <summary>
/// Os tipos de recurso que se alocam a um projecto. **Custos ficam de fora
/// de propósito** — `modules/projects.md` §Conceitos lista "pessoas,
/// viaturas, custos", mas atribuir um custo directo ao projecto implica
/// postar em `finance` (`Regras de negócio`: "Custos de projecto são
/// postados em `finance`; `projects` não escreve no razão"), e o mecanismo
/// de postagem (tempo real ou em lote) é decisão em aberto
/// (`state/pending-decisions.md`). Construir aqui sem essa decisão seria
/// especulativo — mesma disciplina do ADR-040 perante a validação cruzada
/// com o orçamento de `finance`.
/// </summary>
public enum ResourceKind
{
    Employee,
    Vehicle,
}

/// <summary>
/// Alocação de um recurso (Colaborador ou Viatura) a um projecto — parte do
/// agregado <see cref="Project"/> (`modules/projects.md` §Possui).
///
/// <para>
/// <strong>Distinta da atribuição de Tarefa.</strong> Atribuir uma Tarefa
/// (<see cref="ProjectTask.AssignTo"/>) é operacional — "quem faz isto, até
/// quando". Alocar um recurso é ao nível do projecto — "quem/o que está
/// afecto a este projecto, neste período" — para planeamento de capacidade,
/// independente de qualquer tarefa concreta. Os dois podem coexistir sem
/// relação: uma pessoa alocada ao projecto não precisa de ter Tarefas, e uma
/// Tarefa pode ser atribuída a alguém que não está alocado.
/// </para>
///
/// <para>
/// <strong>Referencia o recurso só por identificador</strong> (ADR-010) —
/// `projects` não possui Colaborador nem Viatura (`modules/projects.md`
/// §Não pode); lê-os pelo contrato do módulo dono quando precisar de os
/// mostrar, e nunca copia atributos (BR-18).
/// </para>
/// </summary>
public sealed class ProjectResourceAllocation
{
    internal ProjectResourceAllocation(
        Guid id, Guid projectId, ResourceKind kind, Guid resourceId, DateOnly startsOn)
    {
        Id = id;
        ProjectId = projectId;
        Kind = kind;
        ResourceId = resourceId;
        StartsOn = startsOn;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private ProjectResourceAllocation()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public ResourceKind Kind { get; private set; }

    public Guid ResourceId { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? EndsOn { get; private set; }

    /// <summary>Verdadeiro enquanto a alocação ainda não terminou.</summary>
    public bool IsOpen => EndsOn is null;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    internal void End(DateOnly endsOn)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Esta alocação já terminou.");
        }

        if (endsOn < StartsOn)
        {
            throw new ArgumentException(
                "A data de fim não pode ser anterior ao início da alocação.", nameof(endsOn));
        }

        EndsOn = endsOn;
    }
}
