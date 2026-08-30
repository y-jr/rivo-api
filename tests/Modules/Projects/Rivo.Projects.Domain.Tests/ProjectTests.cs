using Rivo.Projects.Domain;

namespace Rivo.Projects.Domain.Tests;

public class ProjectTests
{
    private static readonly DateOnly Inicio = new(2026, 8, 1);

    private static Project Aberto() => Project.Open("Renovação do armazém", Inicio);

    // --- Open / Close ------------------------------------------------------

    [Fact]
    public void Open_StartsAsActive()
    {
        var projecto = Aberto();

        Assert.Equal(ProjectStatus.Active, projecto.Status);
        Assert.Null(projecto.EndDate);
        Assert.Empty(projecto.Milestones);
        Assert.Empty(projecto.Tasks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Project.Open(name, Inicio));
    }

    [Fact]
    public void Open_TrimsName()
    {
        var projecto = Project.Open("  Renovação  ", Inicio);

        Assert.Equal("Renovação", projecto.Name);
    }

    [Fact]
    public void Close_SetsClosedAndEndDate()
    {
        var projecto = Aberto();
        var fim = Inicio.AddDays(30);

        projecto.Close(fim);

        Assert.Equal(ProjectStatus.Closed, projecto.Status);
        Assert.Equal(fim, projecto.EndDate);
    }

    [Fact]
    public void Close_AlreadyClosed_Throws()
    {
        var projecto = Aberto();
        projecto.Close(Inicio.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => projecto.Close(Inicio.AddDays(2)));
    }

    [Fact]
    public void Close_EndDateBeforeStart_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.Close(Inicio.AddDays(-1)));
    }

    // --- Marco ---------------------------------------------------------

    [Fact]
    public void AddMilestone_AddsAsPending()
    {
        var projecto = Aberto();
        var alvo = Inicio.AddDays(10);

        var marco = projecto.AddMilestone("Fundações prontas", alvo);

        Assert.Equal(projecto.Id, marco.ProjectId);
        Assert.Equal("Fundações prontas", marco.Name);
        Assert.Equal(alvo, marco.TargetDate);
        Assert.Equal(MilestoneStatus.Pending, marco.Status);
        Assert.Null(marco.ReachedOn);
        Assert.Same(marco, Assert.Single(projecto.Milestones));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddMilestone_WithoutName_Throws(string name)
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.AddMilestone(name, Inicio.AddDays(1)));
    }

    [Fact]
    public void AddMilestone_TargetDateBeforeStart_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(
            () => projecto.AddMilestone("Marco impossível", Inicio.AddDays(-1)));
    }

    [Fact]
    public void AddMilestone_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        projecto.Close(Inicio.AddDays(5));

        Assert.Throws<InvalidOperationException>(
            () => projecto.AddMilestone("Marco tardio", Inicio.AddDays(6)));
    }

    [Fact]
    public void ReachMilestone_MarksReachedWithDate()
    {
        var projecto = Aberto();
        var marco = projecto.AddMilestone("Fundações prontas", Inicio.AddDays(10));
        var alcancadoEm = Inicio.AddDays(12);

        projecto.ReachMilestone(marco.Id, alcancadoEm);

        Assert.Equal(MilestoneStatus.Reached, marco.Status);
        Assert.Equal(alcancadoEm, marco.ReachedOn);
    }

    [Fact]
    public void ReachMilestone_AlreadyReached_Throws()
    {
        var projecto = Aberto();
        var marco = projecto.AddMilestone("Fundações prontas", Inicio.AddDays(10));
        projecto.ReachMilestone(marco.Id, Inicio.AddDays(11));

        Assert.Throws<InvalidOperationException>(
            () => projecto.ReachMilestone(marco.Id, Inicio.AddDays(12)));
    }

    [Fact]
    public void ReachMilestone_UnknownId_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<InvalidOperationException>(
            () => projecto.ReachMilestone(Guid.CreateVersion7(), Inicio.AddDays(1)));
    }

    // --- Tarefa ----------------------------------------------------------

    [Fact]
    public void AddTask_WithoutAssignment_StartsAsPendingAndUnassigned()
    {
        var projecto = Aberto();

        var tarefa = projecto.AddTask("Pedir orçamento ao fornecedor", null, null);

        Assert.Equal(projecto.Id, tarefa.ProjectId);
        Assert.Equal("Pedir orçamento ao fornecedor", tarefa.Title);
        Assert.Null(tarefa.DueDate);
        Assert.Null(tarefa.AssignedEmployeeId);
        Assert.Equal(ProjectTaskStatus.Pending, tarefa.Status);
        Assert.True(tarefa.IsOpen);
        Assert.Same(tarefa, Assert.Single(projecto.Tasks));
    }

    [Fact]
    public void AddTask_WithAssignment_SetsAssignedEmployee()
    {
        var projecto = Aberto();
        var colaboradorId = Guid.CreateVersion7();

        var tarefa = projecto.AddTask("Rever planta", Inicio.AddDays(3), colaboradorId);

        Assert.Equal(colaboradorId, tarefa.AssignedEmployeeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddTask_WithoutTitle_Throws(string title)
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.AddTask(title, null, null));
    }

    [Fact]
    public void AddTask_DueDateBeforeStart_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.AddTask("Tarefa impossível", Inicio.AddDays(-1), null));
    }

    [Fact]
    public void AddTask_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        projecto.Close(Inicio.AddDays(5));

        Assert.Throws<InvalidOperationException>(() => projecto.AddTask("Tarefa tardia", null, null));
    }

    [Fact]
    public void AssignTask_ChangesAssignee()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);
        var colaboradorId = Guid.CreateVersion7();

        projecto.AssignTask(tarefa.Id, colaboradorId);

        Assert.Equal(colaboradorId, tarefa.AssignedEmployeeId);
    }

    [Fact]
    public void AssignTask_ToNull_Unassigns()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, Guid.CreateVersion7());

        projecto.AssignTask(tarefa.Id, null);

        Assert.Null(tarefa.AssignedEmployeeId);
    }

    [Fact]
    public void AssignTask_UnknownId_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<InvalidOperationException>(
            () => projecto.AssignTask(Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    [Fact]
    public void CompleteTask_MarksDone()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);

        projecto.CompleteTask(tarefa.Id);

        Assert.Equal(ProjectTaskStatus.Done, tarefa.Status);
        Assert.False(tarefa.IsOpen);
    }

    [Fact]
    public void CompleteTask_AlreadyDone_Throws()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);
        projecto.CompleteTask(tarefa.Id);

        Assert.Throws<InvalidOperationException>(() => projecto.CompleteTask(tarefa.Id));
    }

    [Fact]
    public void CancelTask_MarksCancelled()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);

        projecto.CancelTask(tarefa.Id);

        Assert.Equal(ProjectTaskStatus.Cancelled, tarefa.Status);
    }

    [Fact]
    public void CancelTask_AlreadyDone_Throws()
    {
        // Concluída não se cancela — os dois são estados finais e um não
        // substitui o outro (mesma lógica de BR-14: nunca eliminar, nunca
        // reescrever o facto histórico).
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);
        projecto.CompleteTask(tarefa.Id);

        Assert.Throws<InvalidOperationException>(() => projecto.CancelTask(tarefa.Id));
    }

    [Fact]
    public void AssignTask_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        var tarefa = projecto.AddTask("Rever planta", null, null);
        projecto.Close(Inicio.AddDays(5));

        Assert.Throws<InvalidOperationException>(() => projecto.AssignTask(tarefa.Id, Guid.CreateVersion7()));
    }

    // --- Orçamento ---------------------------------------------------------

    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SetBudget_FirstTime_CreatesIt()
    {
        var projecto = Aberto();

        var orcamento = projecto.SetBudget(500_000m, "aoa", Agora);

        Assert.Equal(projecto.Id, orcamento.ProjectId);
        Assert.Equal(500_000m, orcamento.Amount);
        Assert.Equal("AOA", orcamento.Currency);
        Assert.Equal(Agora, orcamento.SetAt);
        Assert.Same(orcamento, projecto.Budget);
    }

    [Fact]
    public void SetBudget_Again_RevisesTheSameInstance()
    {
        var projecto = Aberto();
        var original = projecto.SetBudget(500_000m, "AOA", Agora);
        var revisao = Agora.AddDays(10);

        projecto.SetBudget(650_000m, "AOA", revisao);

        Assert.Same(original, projecto.Budget);
        Assert.Equal(650_000m, projecto.Budget!.Amount);
        Assert.Equal(revisao, projecto.Budget.SetAt);
    }

    [Fact]
    public void SetBudget_RevisionWithDifferentCurrency_Throws()
    {
        var projecto = Aberto();
        projecto.SetBudget(500_000m, "AOA", Agora);

        Assert.Throws<InvalidOperationException>(() => projecto.SetBudget(1_000m, "USD", Agora.AddDays(1)));

        // A revisão recusada não mexe no orçamento existente.
        Assert.Equal(500_000m, projecto.Budget!.Amount);
        Assert.Equal("AOA", projecto.Budget.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetBudget_NonPositiveAmount_Throws(decimal amount)
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.SetBudget(amount, "AOA", Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AO")]
    [InlineData("KWANZA")]
    public void SetBudget_MalformedCurrency_Throws(string currency)
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() => projecto.SetBudget(1000m, currency, Agora));
    }

    [Fact]
    public void SetBudget_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        projecto.Close(Inicio.AddDays(5));

        Assert.Throws<InvalidOperationException>(() => projecto.SetBudget(1000m, "AOA", Agora));
    }

    [Fact]
    public void Budget_IsNullBeforeSet()
    {
        var projecto = Aberto();

        Assert.Null(projecto.Budget);
    }
}
