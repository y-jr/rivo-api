namespace Rivo.Hr.Domain;

/// <summary>
/// Processo de entrada ou de saída de um Colaborador, conduzido por checklist.
///
/// <para>
/// <strong>Um agregado para os dois, e não dois quase iguais.</strong> Entrada
/// e saída diferem no que a lista contém e em duas datas — a mecânica é a
/// mesma: abrir, executar tarefas, fechar quando não faltar nenhuma. Duplicar
/// a máquina de checklist para mudar o rótulo seria manter duas cópias da
/// mesma regra, e a segunda esqueceria a correcção feita na primeira.
/// </para>
///
/// <para>
/// <strong>Não calcula acertos finais.</strong> O valor a pagar a quem sai
/// depende de proporcionais, férias não gozadas e IRT — cálculo de `payroll`,
/// não de `hr` (<c>.claude/modules/payroll.md</c>).
/// </para>
/// </summary>
public sealed class EmployeeLifecycleProcess
{
    private readonly List<LifecycleTask> _tasks = [];

    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private EmployeeLifecycleProcess() { }

    private EmployeeLifecycleProcess(
        Guid id,
        Guid employeeId,
        LifecycleKind kind,
        DateOnly? lastWorkingDay,
        string? reason)
    {
        Id = id;
        EmployeeId = employeeId;
        Kind = kind;
        LastWorkingDay = lastWorkingDay;
        Reason = reason;
        Status = LifecycleStatus.Pending;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    public LifecycleKind Kind { get; private set; }

    public LifecycleStatus Status { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Último dia de trabalho. Só existe numa saída.</summary>
    public DateOnly? LastWorkingDay { get; private set; }

    /// <summary>Motivo da saída. Só existe numa saída.</summary>
    public string? Reason { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<LifecycleTask> Tasks => _tasks;

    /// <summary>Abre um processo de entrada.</summary>
    public static EmployeeLifecycleProcess StartOnboarding(Guid employeeId)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("O processo pertence sempre a um colaborador.", nameof(employeeId));
        }

        return new EmployeeLifecycleProcess(
            Guid.CreateVersion7(), employeeId, LifecycleKind.Onboarding, null, null);
    }

    /// <summary>
    /// Abre um processo de saída.
    ///
    /// <para>
    /// O último dia de trabalho é obrigatório: é dele que dependem o
    /// vencimento do acesso, a devolução de equipamento e o acerto de contas.
    /// Sem ele, o processo não é accionável.
    /// </para>
    /// </summary>
    public static EmployeeLifecycleProcess StartOffboarding(Guid employeeId, DateOnly lastWorkingDay, string? reason)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("O processo pertence sempre a um colaborador.", nameof(employeeId));
        }

        return new EmployeeLifecycleProcess(
            Guid.CreateVersion7(),
            employeeId,
            LifecycleKind.Offboarding,
            lastWorkingDay,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    /// <summary>
    /// Acrescenta uma tarefa à lista.
    /// </summary>
    public LifecycleTask AddTask(string title, string category, DateOnly? dueOn = null, string? description = null)
    {
        if (Status == LifecycleStatus.Completed)
        {
            throw new InvalidOperationException(
                "Um processo concluído não recebe tarefas novas. Reabra-o primeiro.");
        }

        var task = LifecycleTask.Create(Id, title, category, _tasks.Count, dueOn, description);
        _tasks.Add(task);

        return task;
    }

    /// <summary>Marca o início da execução.</summary>
    public void Begin(DateTimeOffset at)
    {
        if (Status != LifecycleStatus.Pending)
        {
            throw new InvalidOperationException("Este processo já foi iniciado.");
        }

        Status = LifecycleStatus.InProgress;
        StartedAt = at;
    }

    /// <summary>
    /// Conclui uma tarefa da lista. Inicia o processo se ainda estiver por
    /// iniciar — na prática, quem executa a primeira tarefa é quem o começa.
    /// </summary>
    public void CompleteTask(Guid taskId, DateTimeOffset at, Guid? completedBy)
    {
        if (Status == LifecycleStatus.Completed)
        {
            throw new InvalidOperationException("Este processo já foi concluído.");
        }

        var task = _tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new InvalidOperationException("Tarefa não encontrada neste processo.");

        task.Complete(at, completedBy);

        if (Status == LifecycleStatus.Pending)
        {
            Begin(at);
        }
    }

    /// <summary>
    /// Verdadeiro quando não falta nenhuma tarefa.
    /// </summary>
    public bool IsChecklistDone => _tasks.All(t => t.IsCompleted);

    public int PendingTaskCount => _tasks.Count(t => !t.IsCompleted);

    /// <summary>
    /// Conclui o processo.
    ///
    /// <para>
    /// <strong>Recusa-se enquanto faltar uma tarefa.</strong> É esta a regra
    /// que faz o processo valer alguma coisa: sem ela, dar uma saída por
    /// concluída com o portátil por devolver e os acessos por revogar seria um
    /// clique — e é exactamente o que os processos de saída costumam falhar.
    /// </para>
    ///
    /// <para>
    /// Um processo sem tarefas nenhumas também não se conclui: seria dar por
    /// terminado o que nunca foi definido.
    /// </para>
    /// </summary>
    public void Complete(DateTimeOffset at)
    {
        if (Status == LifecycleStatus.Completed)
        {
            throw new InvalidOperationException("Este processo já foi concluído.");
        }

        if (_tasks.Count == 0)
        {
            throw new InvalidOperationException(
                "Um processo sem tarefas não pode ser concluído.");
        }

        if (!IsChecklistDone)
        {
            throw new InvalidOperationException(
                $"Faltam {PendingTaskCount} tarefa(s) por concluir.");
        }

        Status = LifecycleStatus.Completed;
        CompletedAt = at;
    }

    public void Annotate(string? notes) =>
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}

/// <summary>
/// Tarefa de um processo de entrada ou saída.
/// </summary>
public sealed class LifecycleTask
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private LifecycleTask()
    {
        Title = string.Empty;
        Category = string.Empty;
    }

    private LifecycleTask(Guid id, Guid processId, string title, string category, int order, DateOnly? dueOn, string? description)
    {
        Id = id;
        ProcessId = processId;
        Title = title;
        Category = category;
        Order = order;
        DueOn = dueOn;
        Description = description;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid ProcessId { get; private set; }

    public string Title { get; private set; }

    /// <summary>Agrupamento — "acessos", "equipamento", "documentacao".</summary>
    public string Category { get; private set; }

    /// <summary>Ordem de apresentação, atribuída na inserção.</summary>
    public int Order { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public string? Description { get; private set; }

    public bool IsCompleted { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Quem concluiu, para a trilha. Nulo em processos automáticos.</summary>
    public Guid? CompletedBy { get; private set; }

    internal static LifecycleTask Create(
        Guid processId,
        string title,
        string category,
        int order,
        DateOnly? dueOn,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return new LifecycleTask(
            Guid.CreateVersion7(),
            processId,
            title.Trim(),
            category.Trim().ToLowerInvariant(),
            order,
            dueOn,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    internal void Complete(DateTimeOffset at, Guid? completedBy)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("Esta tarefa já está concluída.");
        }

        IsCompleted = true;
        CompletedAt = at;
        CompletedBy = completedBy;
    }
}

public enum LifecycleKind
{
    Onboarding,
    Offboarding,
}

public enum LifecycleStatus
{
    Pending,
    InProgress,
    Completed,
}
