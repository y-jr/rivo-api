namespace Rivo.Projects.Domain;

/// <summary>
/// Projecto. Esqueleto do módulo — ver `modules/projects.md`.
///
/// <para>
/// <strong>Fatia mínima, deliberada.</strong> Marco, Tarefa, Orçamento de
/// Projecto e Alocação de Recursos (ver `modules/projects.md` §Possui) ficam
/// por fazer. Esta entidade é só o contentor — nasce, tem nome e datas, fecha.
/// Sem regra de negócio nenhuma imposta ainda.
/// </para>
/// </summary>
public sealed class Project
{
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
}

public enum ProjectStatus
{
    Active,
    Closed,
}
