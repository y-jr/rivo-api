namespace Rivo.Projects.Domain;

/// <summary>
/// Projecto — agregado raiz de `projects` (ver `modules/projects.md`).
///
/// <para>
/// <strong>Marco, Tarefa, Orçamento e Alocação de Recursos vivem aqui
/// dentro</strong> (§Possui): nascem sempre por este agregado
/// (<see cref="AddMilestone"/>, <see cref="AddTask"/>, <see cref="SetBudget"/>,
/// <see cref="AllocateResource"/>), e é ele que impõe a invariante comum a
/// todos — nada se acrescenta nem se altera depois de o projecto fechar
/// (<see cref="EnsureActive"/>).
/// </para>
/// </summary>
public sealed class Project
{
    private readonly List<Milestone> _milestones = [];
    private readonly List<ProjectTask> _tasks = [];
    private readonly List<ProjectResourceAllocation> _allocations = [];
    private ProjectBudget? _budget;

    private Project(Guid id, string name, DateOnly startDate)
    {
        Id = id;
        Name = name;
        StartDate = startDate;
        Status = ProjectStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Project()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public IReadOnlyList<Milestone> Milestones => _milestones;

    public IReadOnlyList<ProjectTask> Tasks => _tasks;

    public IReadOnlyList<ProjectResourceAllocation> Allocations => _allocations;

    /// <summary>Nulo até <see cref="SetBudget"/> ser chamado a primeira vez.</summary>
    public ProjectBudget? Budget => _budget;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Project Open(string name, DateOnly startDate)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um projecto precisa de nome.", nameof(name));
        }

        return new Project(Guid.CreateVersion7(), name.Trim(), startDate);
    }

    /// <summary>Fecha o projecto. Não há eliminação — o registo fica como facto histórico.</summary>
    public void Close(DateOnly endDate)
    {
        if (Status is ProjectStatus.Closed)
        {
            throw new InvalidOperationException("Este projecto já está fechado.");
        }

        if (endDate < StartDate)
        {
            throw new ArgumentException(
                "A data de fecho não pode ser anterior à de início.", nameof(endDate));
        }

        Status = ProjectStatus.Closed;
        EndDate = endDate;
    }

    /// <summary>
    /// Acrescenta um marco. A data alvo não pode ser anterior ao início do
    /// projecto — um marco "antes de o projecto começar" não é um ponto de
    /// controlo, é um erro de data.
    /// </summary>
    public Milestone AddMilestone(string name, DateOnly targetDate)
    {
        EnsureActive("acrescentar marcos");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um marco precisa de nome.", nameof(name));
        }

        if (targetDate < StartDate)
        {
            throw new ArgumentException(
                "A data do marco não pode ser anterior ao início do projecto.", nameof(targetDate));
        }

        var marco = new Milestone(Guid.CreateVersion7(), Id, name.Trim(), targetDate);
        _milestones.Add(marco);

        return marco;
    }

    public void ReachMilestone(Guid milestoneId, DateOnly reachedOn)
    {
        EnsureActive("alcançar marcos");
        FindMilestone(milestoneId).Reach(reachedOn);
    }

    /// <summary>
    /// Acrescenta uma tarefa, já atribuída ou não.
    ///
    /// <para>
    /// Quem verifica que <paramref name="assignedEmployeeId"/> é um
    /// Colaborador que existe é a camada Application, contra o contrato de
    /// `hr` (ADR-010) — o agregado só sabe que é um identificador.
    /// </para>
    /// </summary>
    public ProjectTask AddTask(string title, DateOnly? dueDate, Guid? assignedEmployeeId)
    {
        EnsureActive("acrescentar tarefas");

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Uma tarefa precisa de título.", nameof(title));
        }

        if (dueDate is { } prazo && prazo < StartDate)
        {
            throw new ArgumentException(
                "O prazo da tarefa não pode ser anterior ao início do projecto.", nameof(dueDate));
        }

        var tarefa = new ProjectTask(Guid.CreateVersion7(), Id, title.Trim(), dueDate, assignedEmployeeId);
        _tasks.Add(tarefa);

        return tarefa;
    }

    public void AssignTask(Guid taskId, Guid? employeeId)
    {
        EnsureActive("atribuir tarefas");
        FindTask(taskId).AssignTo(employeeId);
    }

    public void CompleteTask(Guid taskId)
    {
        EnsureActive("concluir tarefas");
        FindTask(taskId).Complete();
    }

    public void CancelTask(Guid taskId)
    {
        EnsureActive("cancelar tarefas");
        FindTask(taskId).Cancel();
    }

    /// <summary>
    /// Define o orçamento do projecto, ou revê-o se já existir.
    ///
    /// <para>
    /// <strong>A moeda fixa-se na primeira vez.</strong> Uma revisão para
    /// outra moeda é recusada — não porque a conversão seja impossível, mas
    /// porque decidir a taxa de câmbio não é decisão deste método.
    /// </para>
    /// </summary>
    public ProjectBudget SetBudget(decimal amount, string currency, DateTimeOffset at)
    {
        EnsureActive("definir o orçamento");

        if (amount <= 0)
        {
            throw new ArgumentException("O orçamento tem de ser positivo.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217, com três letras.", nameof(currency));
        }

        var moeda = currency.Trim().ToUpperInvariant();

        if (_budget is null)
        {
            _budget = new ProjectBudget(Guid.CreateVersion7(), Id, amount, moeda, at);
        }
        else
        {
            if (_budget.Currency != moeda)
            {
                throw new InvalidOperationException(
                    $"Este projecto já tem orçamento em {_budget.Currency} — uma revisão não muda a moeda.");
            }

            _budget.Revise(amount, at);
        }

        return _budget;
    }

    /// <summary>
    /// Aloca um recurso (Colaborador ou Viatura) ao projecto. Distinta da
    /// atribuição de Tarefa — ver <see cref="ProjectResourceAllocation"/>.
    ///
    /// <para>
    /// Quem verifica que o recurso existe é a camada Application, contra o
    /// contrato do módulo dono (ADR-010) — o agregado só sabe que é um
    /// identificador.
    /// </para>
    /// </summary>
    public ProjectResourceAllocation AllocateResource(ResourceKind kind, Guid resourceId, DateOnly startsOn)
    {
        EnsureActive("alocar recursos");

        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("Uma alocação precisa de um recurso.", nameof(resourceId));
        }

        if (startsOn < StartDate)
        {
            throw new ArgumentException(
                "A alocação não pode começar antes do início do projecto.", nameof(startsOn));
        }

        if (_allocations.Any(a => a.IsOpen && a.Kind == kind && a.ResourceId == resourceId))
        {
            throw new InvalidOperationException(
                "Este recurso já está alocado a este projecto — termine a alocação actual antes de alocar de novo.");
        }

        var alocacao = new ProjectResourceAllocation(Guid.CreateVersion7(), Id, kind, resourceId, startsOn);
        _allocations.Add(alocacao);

        return alocacao;
    }

    public void EndResourceAllocation(Guid allocationId, DateOnly endsOn)
    {
        EnsureActive("terminar alocações");
        FindAllocation(allocationId).End(endsOn);
    }

    private Milestone FindMilestone(Guid milestoneId) =>
        _milestones.FirstOrDefault(m => m.Id == milestoneId)
            ?? throw new InvalidOperationException("Marco não encontrado neste projecto.");

    private ProjectTask FindTask(Guid taskId) =>
        _tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new InvalidOperationException("Tarefa não encontrada neste projecto.");

    private ProjectResourceAllocation FindAllocation(Guid allocationId) =>
        _allocations.FirstOrDefault(a => a.Id == allocationId)
            ?? throw new InvalidOperationException("Alocação não encontrada neste projecto.");

    /// <summary>
    /// Fechado é facto histórico: nada se acrescenta nem se altera depois —
    /// mesma leitura que impede reabrir o projecto em <see cref="Close"/>.
    /// </summary>
    private void EnsureActive(string acto)
    {
        if (Status is ProjectStatus.Closed)
        {
            throw new InvalidOperationException($"Não é possível {acto}: o projecto está fechado.");
        }
    }
}

public enum ProjectStatus
{
    Active,
    Closed,
}
