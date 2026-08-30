namespace Rivo.Projects.Domain;

/// <summary>
/// Tarefa de um projecto — atribuição, prazo e estado, parte do agregado
/// <see cref="Project"/> (`modules/projects.md` §Possui).
///
/// <para>
/// <strong>O nome evita <c>Task</c> de propósito</strong> — colidiria de
/// vista com <c>System.Threading.Tasks.Task</c> em todo o módulo.
/// </para>
///
/// <para>
/// <strong>A atribuição referencia um Colaborador só por identificador</strong>
/// (ADR-010) — `projects` não copia nome, departamento nem cargo (BR-18);
/// lê-os pelo contrato de `hr` quando precisar de os mostrar.
/// </para>
/// </summary>
public sealed class ProjectTask
{
    internal ProjectTask(Guid id, Guid projectId, string title, DateOnly? dueDate, Guid? assignedEmployeeId)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        DueDate = dueDate;
        AssignedEmployeeId = assignedEmployeeId;
        Status = ProjectTaskStatus.Pending;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private ProjectTask()
    {
        Title = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public ProjectTaskStatus Status { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    /// <summary>Verdadeiro enquanto a tarefa ainda não chegou a um estado final.</summary>
    public bool IsOpen => Status is ProjectTaskStatus.Pending;

    /// <summary>
    /// Atribui ou desatribui (<paramref name="employeeId"/> nulo) a tarefa.
    /// Quem verifica que o Colaborador existe é a camada Application, contra
    /// o contrato de `hr` — o domínio não fala com outro módulo.
    /// </summary>
    internal void AssignTo(Guid? employeeId)
    {
        EnsureOpen("atribuir");
        AssignedEmployeeId = employeeId;
    }

    internal void Complete()
    {
        EnsureOpen("concluir");
        Status = ProjectTaskStatus.Done;
    }

    /// <summary>Nunca elimina — BR-14. Uma tarefa cancelada fica como facto histórico.</summary>
    internal void Cancel()
    {
        EnsureOpen("cancelar");
        Status = ProjectTaskStatus.Cancelled;
    }

    private void EnsureOpen(string acto)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"Não é possível {acto}: a tarefa está em {Status}.");
        }
    }
}

public enum ProjectTaskStatus
{
    Pending,
    Done,
    Cancelled,
}
