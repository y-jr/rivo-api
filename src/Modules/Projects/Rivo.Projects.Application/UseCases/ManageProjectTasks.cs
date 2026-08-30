using Rivo.Audit.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.UseCases;

/// <summary>
/// Acrescenta uma tarefa, atribuída ou não.
///
/// <para>
/// <strong>Quando há atribuição, o Colaborador tem de existir em `hr`</strong>
/// (ADR-010) — lido pelo contrato, nunca copiado (BR-18). Sem esta
/// verificação, uma tarefa podia ficar atribuída a um identificador que não é
/// ninguém, e só se descobriria ao tentar mostrar o nome.
/// </para>
/// </summary>
public sealed class AddTask(IProjectStore store, IEmployeeDirectory employees, IAuditTrail audit, TimeProvider clock)
{
    public async Task<AddTaskResult> ExecuteAsync(
        Guid projectId,
        string title,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return AddTaskResult.NotFound();
        }

        if (assignedEmployeeId is { } employeeId)
        {
            var colaborador = await employees.FindAsync(employeeId, clock.GetUtcNow(), cancellationToken);

            if (colaborador is null)
            {
                return AddTaskResult.EmployeeNotFound();
            }
        }

        ProjectTask tarefa;

        try
        {
            tarefa = projecto.AddTask(title, dueDate, assignedEmployeeId);
        }
        catch (ArgumentException error)
        {
            return AddTaskResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Projecto fechado: conflito com o estado actual, não pedido
            // malformado — 409, não 400.
            return AddTaskResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.TaskAdded,
                ProjectsAuditEntityTypes.Task,
                tarefa.Id.ToString(),
                context,
                NewValue: $$"""{"projectId":"{{projectId}}","title":"{{tarefa.Title}}","assignedEmployeeId":{{(tarefa.AssignedEmployeeId is { } id ? $"\"{id}\"" : "null")}}}"""),
            cancellationToken);

        return AddTaskResult.Success(tarefa.Id);
    }
}

public sealed record AddTaskResult(AddTaskOutcome Outcome, Guid? TaskId, string? Error)
{
    public static AddTaskResult Success(Guid taskId) => new(AddTaskOutcome.Added, taskId, null);

    public static AddTaskResult NotFound() =>
        new(AddTaskOutcome.NotFound, null, "Projecto não encontrado.");

    public static AddTaskResult EmployeeNotFound() =>
        new(AddTaskOutcome.EmployeeNotFound, null, "Colaborador a atribuir não encontrado.");

    public static AddTaskResult Rejected(string error) => new(AddTaskOutcome.Rejected, null, error);

    public static AddTaskResult Conflict(string error) => new(AddTaskOutcome.Conflict, null, error);
}

public enum AddTaskOutcome
{
    Added,
    NotFound,
    EmployeeNotFound,

    /// <summary>Pedido malformado — título vazio ou prazo antes do início. 400.</summary>
    Rejected,

    /// <summary>Projecto fechado. 409.</summary>
    Conflict,
}

/// <summary>Atribui ou desatribui (<c>employeeId</c> nulo) uma tarefa já existente.</summary>
public sealed class AssignTask(IProjectStore store, IEmployeeDirectory employees, IAuditTrail audit, TimeProvider clock)
{
    public async Task<AssignTaskOutcome> ExecuteAsync(
        Guid projectId,
        Guid taskId,
        Guid? employeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return AssignTaskOutcome.ProjectNotFound;
        }

        if (projecto.Tasks.All(t => t.Id != taskId))
        {
            return AssignTaskOutcome.TaskNotFound;
        }

        if (employeeId is { } id)
        {
            var colaborador = await employees.FindAsync(id, clock.GetUtcNow(), cancellationToken);

            if (colaborador is null)
            {
                return AssignTaskOutcome.EmployeeNotFound;
            }
        }

        try
        {
            projecto.AssignTask(taskId, employeeId);
        }
        catch (InvalidOperationException)
        {
            return AssignTaskOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.TaskAssigned,
                ProjectsAuditEntityTypes.Task,
                taskId.ToString(),
                context,
                NewValue: $$"""{"assignedEmployeeId":{{(employeeId is { } eid ? $"\"{eid}\"" : "null")}}}"""),
            cancellationToken);

        return AssignTaskOutcome.Assigned;
    }
}

public enum AssignTaskOutcome
{
    Assigned,
    ProjectNotFound,
    TaskNotFound,
    EmployeeNotFound,

    /// <summary>A tarefa já não está aberta (concluída ou cancelada), ou o projecto está fechado.</summary>
    Rejected,
}

public sealed class CompleteTask(IProjectStore store, IAuditTrail audit)
{
    public async Task<TaskLifecycleOutcome> ExecuteAsync(
        Guid projectId, Guid taskId, AuditContext context, CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return TaskLifecycleOutcome.ProjectNotFound;
        }

        if (projecto.Tasks.All(t => t.Id != taskId))
        {
            return TaskLifecycleOutcome.TaskNotFound;
        }

        try
        {
            projecto.CompleteTask(taskId);
        }
        catch (InvalidOperationException)
        {
            return TaskLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.TaskCompleted, ProjectsAuditEntityTypes.Task, taskId.ToString(), context),
            cancellationToken);

        return TaskLifecycleOutcome.Applied;
    }
}

/// <summary>Nunca elimina — BR-14. Cancelar deixa a tarefa como facto histórico.</summary>
public sealed class CancelTask(IProjectStore store, IAuditTrail audit)
{
    public async Task<TaskLifecycleOutcome> ExecuteAsync(
        Guid projectId, Guid taskId, AuditContext context, CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return TaskLifecycleOutcome.ProjectNotFound;
        }

        if (projecto.Tasks.All(t => t.Id != taskId))
        {
            return TaskLifecycleOutcome.TaskNotFound;
        }

        try
        {
            projecto.CancelTask(taskId);
        }
        catch (InvalidOperationException)
        {
            return TaskLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.TaskCancelled, ProjectsAuditEntityTypes.Task, taskId.ToString(), context),
            cancellationToken);

        return TaskLifecycleOutcome.Applied;
    }
}

public enum TaskLifecycleOutcome
{
    Applied,
    ProjectNotFound,
    TaskNotFound,

    /// <summary>A tarefa já não está aberta — concluir ou cancelar não valem duas vezes.</summary>
    Rejected,
}
