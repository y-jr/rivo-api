namespace Rivo.Audit.Domain;

/// <summary>
/// Um facto histórico: quem fez o quê, quando, sobre que registo.
///
/// <para>
/// <strong>Imutável por construção.</strong> Não há métodos que alterem
/// estado, e os setters são privados. A imutabilidade é a única regra de
/// domínio de `audit` — o módulo não decide o que é auditável nem interpreta o
/// significado do que regista; isso pertence ao módulo de origem.
/// </para>
///
/// <para>
/// A garantia append-only ao nível da base de dados (permissões, revogação de
/// UPDATE/DELETE) ainda não está implementada — ver state/known-issues.md.
/// </para>
/// </summary>
public sealed class AuditEvent
{
    // Construtor sem parâmetros exigido pelo EF Core para materialização.
    private AuditEvent()
    {
        Action = string.Empty;
        EntityType = string.Empty;
        EntityId = string.Empty;
    }

    private AuditEvent(
        Guid id,
        string action,
        string entityType,
        string entityId,
        Guid? actorId,
        string? ipAddress,
        string? correlationId,
        string? previousValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        Id = id;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        ActorId = actorId;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
        PreviousValue = previousValue;
        NewValue = newValue;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    /// <summary>Nulo em acções sem utilizador autenticado ou de processos automáticos.</summary>
    public Guid? ActorId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? PreviousValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditEvent Record(
        string action,
        string entityType,
        string entityId,
        Guid? actorId,
        string? ipAddress,
        string? correlationId,
        string? previousValue,
        string? newValue,
        DateTimeOffset occurredAt)
    {
        // Um registo sem acção ou sem alvo não é auditável — seria ruído que
        // dá a impressão de haver trilha onde não há.
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        return new AuditEvent(
            id: Guid.CreateVersion7(),
            action: action,
            entityType: entityType,
            entityId: entityId,
            actorId: actorId,
            ipAddress: ipAddress,
            correlationId: correlationId,
            previousValue: previousValue,
            newValue: newValue,
            occurredAt: occurredAt);
    }
}
