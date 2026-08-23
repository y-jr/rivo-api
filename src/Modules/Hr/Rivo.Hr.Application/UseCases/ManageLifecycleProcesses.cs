using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Lista processos de entrada e de saída, com o progresso da checklist.
/// </summary>
public sealed class ListLifecycleProcesses(IHrStore store)
{
    public static readonly IReadOnlyList<string> Kinds = [.. Enum.GetNames<LifecycleKind>()];

    public async Task<IReadOnlyList<LifecycleProcessView>> ExecuteAsync(
        string? kind,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        LifecycleKind? filter = null;

        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<LifecycleKind>(kind, ignoreCase: true, out var parsed))
            {
                return [];
            }

            filter = parsed;
        }

        var processes = await store.ListLifecycleProcessesAsync(filter, employeeId, cancellationToken);

        return [.. processes.Select(Project)];
    }

    internal static LifecycleProcessView Project(EmployeeLifecycleProcess p) =>
        new(p.Id,
            p.EmployeeId,
            p.Kind.ToString(),
            p.Status.ToString(),
            p.StartedAt,
            p.CompletedAt,
            p.LastWorkingDay,
            p.Reason,
            p.Notes,
            p.PendingTaskCount,
            [.. p.Tasks
                .OrderBy(t => t.Order)
                .Select(t => new LifecycleTaskView(
                    t.Id, t.Title, t.Category, t.Order, t.DueOn,
                    t.Description, t.IsCompleted, t.CompletedAt, t.CompletedBy))]);
}

/// <param name="PendingTasks">
/// Quantas faltam. É o que impede concluir o processo, e o que a interface
/// mostra como progresso.
/// </param>
public sealed record LifecycleProcessView(
    Guid ProcessId,
    Guid EmployeeId,
    string Kind,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateOnly? LastWorkingDay,
    string? Reason,
    string? Notes,
    int PendingTasks,
    IReadOnlyList<LifecycleTaskView> Tasks);

public sealed record LifecycleTaskView(
    Guid TaskId,
    string Title,
    string Category,
    int Order,
    DateOnly? DueOn,
    string? Description,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    Guid? CompletedBy);

/// <summary>
/// Abre um processo de entrada ou de saída, com as tarefas iniciais.
///
/// <para>
/// As tarefas vêm no mesmo pedido em vez de serem acrescentadas uma a uma:
/// abrir um processo vazio e esquecer-se de o preencher produz exactamente a
/// lista de verificação que não verifica nada.
/// </para>
/// </summary>
public sealed class StartLifecycleProcess(IHrStore store, IAuditTrail audit)
{
    public async Task<LifecycleResult> ExecuteAsync(
        Guid employeeId,
        string kind,
        DateOnly? lastWorkingDay,
        string? reason,
        IReadOnlyList<NewLifecycleTask> tasks,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LifecycleKind>(kind, ignoreCase: true, out var lifecycleKind))
        {
            return LifecycleResult.Rejected(
                $"Tipo de processo desconhecido. Esperado: {string.Join(", ", ListLifecycleProcesses.Kinds)}.");
        }

        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return LifecycleResult.NotFound("Colaborador não encontrado.");
        }

        EmployeeLifecycleProcess process;

        try
        {
            process = lifecycleKind switch
            {
                LifecycleKind.Offboarding when lastWorkingDay is null =>
                    throw new ArgumentException("Um processo de saída exige o último dia de trabalho."),

                LifecycleKind.Offboarding =>
                    EmployeeLifecycleProcess.StartOffboarding(employeeId, lastWorkingDay!.Value, reason),

                _ => EmployeeLifecycleProcess.StartOnboarding(employeeId),
            };

            foreach (var task in tasks)
            {
                process.AddTask(task.Title, task.Category, task.DueOn, task.Description);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return LifecycleResult.Rejected(error.Message);
        }

        await store.AddLifecycleProcessAsync(process, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.LifecycleStarted,
                HrAuditEntityTypes.LifecycleProcess,
                process.Id.ToString(),
                context,
                NewValue: $$"""{"kind":"{{lifecycleKind}}","employeeId":"{{employeeId}}"}"""),
            cancellationToken);

        return LifecycleResult.Success(process.Id);
    }
}

public sealed record NewLifecycleTask(string Title, string Category, DateOnly? DueOn, string? Description);

/// <summary>
/// Conclui uma tarefa da checklist.
/// </summary>
public sealed class CompleteLifecycleTask(IHrStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<LifecycleResult> ExecuteAsync(
        Guid processId,
        Guid taskId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var process = await store.FindLifecycleProcessAsync(processId, cancellationToken);

        if (process is null)
        {
            return LifecycleResult.NotFound("Processo não encontrado.");
        }

        try
        {
            // O actor da auditoria é quem concluiu — a mesma pessoa, registada
            // nos dois sítios sem a pedir duas vezes.
            process.CompleteTask(taskId, clock.GetUtcNow(), context.ActorId);
        }
        catch (InvalidOperationException error)
        {
            return LifecycleResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.LifecycleTaskCompleted,
                HrAuditEntityTypes.LifecycleProcess,
                process.Id.ToString(),
                context,
                NewValue: $$"""{"taskId":"{{taskId}}","pending":{{process.PendingTaskCount}}}"""),
            cancellationToken);

        return LifecycleResult.Success(process.Id);
    }
}

/// <summary>
/// Conclui o processo. <strong>Recusa-se enquanto faltar uma tarefa</strong> —
/// a regra vive no domínio, aqui só se traduz a recusa.
/// </summary>
public sealed class CompleteLifecycleProcess(IHrStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<LifecycleResult> ExecuteAsync(
        Guid processId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var process = await store.FindLifecycleProcessAsync(processId, cancellationToken);

        if (process is null)
        {
            return LifecycleResult.NotFound("Processo não encontrado.");
        }

        try
        {
            process.Complete(clock.GetUtcNow());
        }
        catch (InvalidOperationException error)
        {
            return LifecycleResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.LifecycleCompleted,
                HrAuditEntityTypes.LifecycleProcess,
                process.Id.ToString(),
                context),
            cancellationToken);

        return LifecycleResult.Success(process.Id);
    }
}

public sealed record LifecycleResult(LifecycleOutcome Outcome, Guid? ProcessId, string? Error)
{
    public static LifecycleResult Success(Guid id) => new(LifecycleOutcome.Done, id, null);

    public static LifecycleResult NotFound(string reason) => new(LifecycleOutcome.NotFound, null, reason);

    public static LifecycleResult Rejected(string reason) => new(LifecycleOutcome.Rejected, null, reason);
}

public enum LifecycleOutcome
{
    Done,
    NotFound,
    Rejected,
}
