namespace Rivo.Audit.Application;

/// <summary>Consulta da trilha, para relatório e investigação.</summary>
public sealed class QueryAuditTrail(IAuditEventStore store)
{
    /// <summary>Tecto absoluto, para que um pedido não devolva a trilha inteira.</summary>
    private const int MaxLimit = 200;

    public async Task<IReadOnlyList<AuditEntryView>> ExecuteAsync(
        string? entityType,
        string? entityId,
        int limit,
        CancellationToken cancellationToken)
    {
        var capped = Math.Clamp(limit, 1, MaxLimit);

        var entries = await store.QueryAsync(entityType, entityId, capped, cancellationToken);

        return [.. entries.Select(entry => new AuditEntryView(
            entry.Id,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.ActorId,
            entry.IpAddress,
            entry.CorrelationId,
            entry.OccurredAt))];
    }
}

/// <summary>
/// Vista da trilha. Omite deliberadamente <c>PreviousValue</c> e
/// <c>NewValue</c>: podem conter dados sensíveis, e expô-los numa listagem
/// geral seria contrário à minimização de dados (BR-16).
/// </summary>
public sealed record AuditEntryView(
    Guid Id,
    string Action,
    string EntityType,
    string EntityId,
    Guid? ActorId,
    string? IpAddress,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
