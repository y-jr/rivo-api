using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Invariantes dos processos de entrada e de saída.
///
/// <para>
/// A regra que dá sentido ao agregado é uma só: <strong>não se conclui um
/// processo com tarefas por fazer</strong>. É o que separa uma checklist de uma
/// decoração.
/// </para>
/// </summary>
public class EmployeeLifecycleProcessTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly LastDay = new(2026, 9, 30);

    private static EmployeeLifecycleProcess Onboarding() =>
        EmployeeLifecycleProcess.StartOnboarding(Employee);

    private static EmployeeLifecycleProcess Offboarding() =>
        EmployeeLifecycleProcess.StartOffboarding(Employee, LastDay, "Rescisão por mútuo acordo");

    [Fact]
    public void StartOnboarding_HasNoOffboardingFields()
    {
        var process = Onboarding();

        Assert.Equal(LifecycleKind.Onboarding, process.Kind);
        Assert.Equal(LifecycleStatus.Pending, process.Status);
        Assert.Null(process.LastWorkingDay);
        Assert.Null(process.Reason);
    }

    [Fact]
    public void StartOffboarding_RequiresTheLastWorkingDay()
    {
        var process = Offboarding();

        Assert.Equal(LifecycleKind.Offboarding, process.Kind);
        Assert.Equal(LastDay, process.LastWorkingDay);
    }

    [Fact]
    public void AddTask_AssignsSequentialOrder()
    {
        var process = Onboarding();

        process.AddTask("Criar conta de e-mail", "acessos");
        process.AddTask("Entregar portátil", "equipamento");

        Assert.Equal([0, 1], process.Tasks.Select(t => t.Order));
        Assert.Equal("acessos", process.Tasks[0].Category);
    }

    [Fact]
    public void AddTask_WithoutTitle_IsRejected()
    {
        var process = Onboarding();

        Assert.Throws<ArgumentException>(() => process.AddTask("  ", "acessos"));
    }

    /// <summary>
    /// Quem executa a primeira tarefa é quem começa o processo, na prática.
    /// </summary>
    [Fact]
    public void CompleteTask_StartsAPendingProcess()
    {
        var process = Onboarding();
        var task = process.AddTask("Criar conta", "acessos");

        process.CompleteTask(task.Id, Now, null);

        Assert.Equal(LifecycleStatus.InProgress, process.Status);
        Assert.Equal(Now, process.StartedAt);
    }

    [Fact]
    public void CompleteTask_RecordsWhoAndWhen()
    {
        var process = Onboarding();
        var task = process.AddTask("Criar conta", "acessos");
        var actor = Guid.CreateVersion7();

        process.CompleteTask(task.Id, Now, actor);

        Assert.True(task.IsCompleted);
        Assert.Equal(Now, task.CompletedAt);
        Assert.Equal(actor, task.CompletedBy);
    }

    [Fact]
    public void CompleteTask_Twice_IsRejected()
    {
        var process = Onboarding();
        var task = process.AddTask("Criar conta", "acessos");
        process.CompleteTask(task.Id, Now, null);

        Assert.Throws<InvalidOperationException>(() => process.CompleteTask(task.Id, Now, null));
    }

    [Fact]
    public void CompleteTask_UnknownTask_IsRejected()
    {
        var process = Onboarding();

        Assert.Throws<InvalidOperationException>(() =>
            process.CompleteTask(Guid.CreateVersion7(), Now, null));
    }

    /// <summary>
    /// <strong>A regra central.</strong> Dar uma saída por concluída com o
    /// portátil por devolver e os acessos por revogar é exactamente o que estes
    /// processos costumam falhar.
    /// </summary>
    [Fact]
    public void Complete_WithPendingTasks_IsRejected()
    {
        var process = Offboarding();
        process.AddTask("Revogar acessos", "acessos");
        process.AddTask("Recolher portátil", "equipamento");

        var first = process.Tasks[0];
        process.CompleteTask(first.Id, Now, null);

        var error = Assert.Throws<InvalidOperationException>(() => process.Complete(Now));
        Assert.Contains("1", error.Message);
        Assert.Equal(1, process.PendingTaskCount);
    }

    [Fact]
    public void Complete_WithEveryTaskDone_Succeeds()
    {
        var process = Offboarding();
        process.AddTask("Revogar acessos", "acessos");
        process.AddTask("Recolher portátil", "equipamento");

        foreach (var task in process.Tasks.ToList())
        {
            process.CompleteTask(task.Id, Now, null);
        }

        process.Complete(Now);

        Assert.Equal(LifecycleStatus.Completed, process.Status);
        Assert.Equal(Now, process.CompletedAt);
        Assert.True(process.IsChecklistDone);
    }

    /// <summary>
    /// Concluir o que nunca foi definido não quer dizer nada — e permitiria
    /// fechar um processo abrindo-o e fechando-o de imediato.
    /// </summary>
    [Fact]
    public void Complete_WithNoTasksAtAll_IsRejected()
    {
        var process = Onboarding();

        Assert.Throws<InvalidOperationException>(() => process.Complete(Now));
    }

    [Fact]
    public void Complete_Twice_IsRejected()
    {
        var process = Onboarding();
        var task = process.AddTask("Criar conta", "acessos");
        process.CompleteTask(task.Id, Now, null);
        process.Complete(Now);

        Assert.Throws<InvalidOperationException>(() => process.Complete(Now));
    }

    [Fact]
    public void AddTask_ToCompletedProcess_IsRejected()
    {
        var process = Onboarding();
        var task = process.AddTask("Criar conta", "acessos");
        process.CompleteTask(task.Id, Now, null);
        process.Complete(Now);

        Assert.Throws<InvalidOperationException>(() => process.AddTask("Tardia", "acessos"));
    }

    [Fact]
    public void CompleteTask_OnCompletedProcess_IsRejected()
    {
        var process = Onboarding();
        var first = process.AddTask("Criar conta", "acessos");
        process.CompleteTask(first.Id, Now, null);
        process.Complete(Now);

        Assert.Throws<InvalidOperationException>(() => process.CompleteTask(first.Id, Now, null));
    }

    [Fact]
    public void Begin_Twice_IsRejected()
    {
        var process = Onboarding();
        process.Begin(Now);

        Assert.Throws<InvalidOperationException>(() => process.Begin(Now));
    }
}
