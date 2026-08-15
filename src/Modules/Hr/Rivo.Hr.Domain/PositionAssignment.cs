namespace Rivo.Hr.Domain;

/// <summary>
/// Quem ocupa que Cargo, e quando.
///
/// <para>
/// <strong>É histórica, não uma coluna em Colaborador</strong> (ADR-005). Um
/// cargo é ocupado por alguém num período. `approval` precisa de saber quem o
/// ocupava <em>à data da submissão</em>, não hoje — sem isso, uma mudança
/// organizacional recalcularia processos em curso (BR-6).
/// </para>
/// </summary>
public sealed class PositionAssignment
{
    private PositionAssignment() { }

    private PositionAssignment(
        Guid id,
        Guid employeeId,
        Guid positionId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        PositionAssignmentStatus status)
    {
        Id = id;
        EmployeeId = employeeId;
        PositionId = positionId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Status = status;
    }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid PositionId { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    /// <summary>Aberto no fim enquanto o colaborador ocupar o Cargo.</summary>
    public DateTimeOffset? EffectiveTo { get; private set; }

    public PositionAssignmentStatus Status { get; private set; }

    /// <summary>
    /// Atribuição de um Cargo que <strong>não</strong> confere autoridade de
    /// aprovação. Produz efeito de imediato (ADR-015).
    /// </summary>
    public static PositionAssignment CreateEffective(
        Guid employeeId,
        Guid positionId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        Validate(employeeId, positionId, effectiveFrom, effectiveTo);

        return new PositionAssignment(
            Guid.CreateVersion7(), employeeId, positionId, effectiveFrom, effectiveTo,
            PositionAssignmentStatus.Effective);
    }

    /// <summary>
    /// Atribuição de um Cargo que confere autoridade de aprovação. Nasce
    /// <see cref="PositionAssignmentStatus.Pending"/> e só passa a efectiva
    /// depois de decisão "Aprovado" (BR-20).
    ///
    /// <para>
    /// Uma atribuição pendente <strong>não confere autoridade nenhuma</strong>
    /// — é isso que fecha o caminho de escalada de privilégios descrito em
    /// ADR-015.
    /// </para>
    /// </summary>
    public static PositionAssignment CreatePending(
        Guid employeeId,
        Guid positionId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        Validate(employeeId, positionId, effectiveFrom, effectiveTo);

        return new PositionAssignment(
            Guid.CreateVersion7(), employeeId, positionId, effectiveFrom, effectiveTo,
            PositionAssignmentStatus.Pending);
    }

    /// <summary>
    /// Verifica se a atribuição está em vigor à data indicada.
    ///
    /// Só atribuições efectivas contam: uma pendente existe, mas não confere
    /// o Cargo.
    /// </summary>
    public bool IsEffectiveAt(DateTimeOffset instant) =>
        Status is PositionAssignmentStatus.Effective
        && instant >= EffectiveFrom
        && (EffectiveTo is null || instant < EffectiveTo);

    /// <summary>Termina a ocupação do Cargo.</summary>
    public void End(DateTimeOffset endsAt)
    {
        if (endsAt < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAt), "Uma atribuição não pode terminar antes de começar.");
        }

        EffectiveTo = endsAt;
    }

    private static void Validate(Guid employeeId, Guid positionId, DateTimeOffset from, DateTimeOffset? to)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A atribuição tem de pertencer a um colaborador.", nameof(employeeId));
        }

        if (positionId == Guid.Empty)
        {
            throw new ArgumentException("A atribuição tem de referir um cargo.", nameof(positionId));
        }

        if (to is not null && to <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "O fim tem de ser posterior ao início.");
        }
    }
}

public enum PositionAssignmentStatus
{
    /// <summary>Submetida, à espera de decisão. Não confere o Cargo.</summary>
    Pending,

    /// <summary>Em vigor no período indicado.</summary>
    Effective,

    /// <summary>Recusada em aprovação. Conservada para auditoria.</summary>
    Rejected,
}
