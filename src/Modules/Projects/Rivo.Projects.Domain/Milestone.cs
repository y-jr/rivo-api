namespace Rivo.Projects.Domain;

/// <summary>
/// Marco de um projecto — um ponto de controlo com data alvo, parte do
/// agregado <see cref="Project"/> (`modules/projects.md` §Possui).
///
/// <para>
/// Nasce sempre por <see cref="Project.AddMilestone"/>: não tem vida fora do
/// projecto a que pertence, por isso o construtor é <c>internal</c> — só o
/// agregado o chama — e a eliminação em cascata (ver a configuração EF) segue
/// a mesma regra.
/// </para>
/// </summary>
public sealed class Milestone
{
    internal Milestone(Guid id, Guid projectId, string name, DateOnly targetDate)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        TargetDate = targetDate;
        Status = MilestoneStatus.Pending;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Milestone()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public DateOnly TargetDate { get; private set; }

    public MilestoneStatus Status { get; private set; }

    public DateOnly? ReachedOn { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Marca o marco como alcançado.
    ///
    /// <para>
    /// Vale uma vez só — um marco alcançado não volta a "por alcançar": é
    /// facto histórico, na mesma lógica de <see cref="Project.Close"/>.
    /// </para>
    /// </summary>
    internal void Reach(DateOnly reachedOn)
    {
        if (Status is MilestoneStatus.Reached)
        {
            throw new InvalidOperationException("Este marco já foi alcançado.");
        }

        Status = MilestoneStatus.Reached;
        ReachedOn = reachedOn;
    }
}

public enum MilestoneStatus
{
    Pending,
    Reached,
}
