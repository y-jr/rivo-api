namespace Rivo.Hr.Domain;

/// <summary>
/// Unidade organizacional.
///
/// <strong>Distinto de Centro de Custo</strong>, que pertence a `finance`
/// (ADR-005): o mapeamento entre os dois é opcional e não é 1:1. Nem todo o
/// centro de custo corresponde a um departamento.
/// </summary>
public sealed class Department
{
    private Department() => Name = string.Empty;

    private Department(Guid id, string name, Guid? managerId)
    {
        Id = id;
        Name = name;
        ManagerId = managerId;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Gestor do departamento. Referência a Colaborador, opcional: um
    /// departamento pode existir antes de lhe ser atribuído um gestor.
    /// </summary>
    public Guid? ManagerId { get; private set; }

    public static Department Create(string name, Guid? managerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Department(Guid.CreateVersion7(), name.Trim(), managerId);
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void AssignManager(Guid? managerId) => ManagerId = managerId;
}
