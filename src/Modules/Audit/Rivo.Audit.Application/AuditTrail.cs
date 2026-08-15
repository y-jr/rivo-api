using Rivo.Audit.Contracts;
using Rivo.Audit.Domain;

namespace Rivo.Audit.Application;

/// <summary>
/// Implementa o contrato publicado. Converte o pedido do módulo de origem num
/// facto de domínio e persiste-o.
///
/// Não interpreta nem valida o significado de negócio do que regista: se o
/// módulo de origem diz que a acção aconteceu, `audit` regista.
/// </summary>
public sealed class AuditTrail(IAuditEventStore store, TimeProvider clock) : IAuditTrail
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var entry = AuditEvent.Record(
            action: record.Action,
            entityType: record.EntityType,
            entityId: record.EntityId,
            actorId: record.Context.ActorId,
            ipAddress: record.Context.IpAddress,
            correlationId: record.Context.CorrelationId,
            previousValue: record.PreviousValue,
            newValue: record.NewValue,
            occurredAt: clock.GetUtcNow());

        await store.AddAsync(entry, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Persistência da trilha. Implementada em Infrastructure.</summary>
public interface IAuditEventStore
{
    Task AddAsync(AuditEvent entry, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <param name="entityType">Filtro opcional por tipo de entidade.</param>
    /// <param name="entityId">Filtro opcional por registo concreto.</param>
    /// <param name="limit">Tecto de resultados, para não devolver a trilha inteira.</param>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        string? entityType,
        string? entityId,
        int limit,
        CancellationToken cancellationToken);
}
