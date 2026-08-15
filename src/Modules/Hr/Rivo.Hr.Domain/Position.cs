namespace Rivo.Hr.Domain;

/// <summary>
/// Cargo — posição organizacional ("Director Financeiro", "DAF", "CEO").
///
/// <para>
/// <strong>Distinto de Perfil de Acesso</strong>, que pertence a `identity`
/// (ADR-005). O Perfil responde a "o que pode ver no sistema"; o Cargo
/// responde a "que posição ocupa na organização". Um contabilista e um
/// Director Financeiro podem ter ambos o perfil `Finance`, mas só um deles
/// aprova acima de determinada alçada.
/// </para>
/// </summary>
public sealed class Position
{
    private Position() => Name = string.Empty;

    private Position(Guid id, string name, int hierarchyLevel, bool grantsApprovalAuthority)
    {
        Id = id;
        Name = name;
        HierarchyLevel = hierarchyLevel;
        GrantsApprovalAuthority = grantsApprovalAuthority;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Menor é mais alto na hierarquia. Usado para escalonamento em `approval`.</summary>
    public int HierarchyLevel { get; private set; }

    /// <summary>
    /// Se este Cargo confere autoridade para aprovar.
    ///
    /// <para>
    /// É a marca mais sensível do módulo: `approval` resolve aprovadores por
    /// Cargo, logo quem controla esta marca controla, indirectamente, quem
    /// pode aprovar pagamentos. Só alterável por `Admin`, e auditada (BR-21).
    /// </para>
    ///
    /// <para>
    /// Atribuir um Cargo com esta marca exige aprovação prévia (BR-20).
    /// </para>
    /// </summary>
    public bool GrantsApprovalAuthority { get; private set; }

    public static Position Create(string name, int hierarchyLevel, bool grantsApprovalAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(hierarchyLevel);

        return new Position(Guid.CreateVersion7(), name.Trim(), hierarchyLevel, grantsApprovalAuthority);
    }
}
