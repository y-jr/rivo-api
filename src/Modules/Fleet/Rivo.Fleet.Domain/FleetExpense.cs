namespace Rivo.Fleet.Domain;

/// <summary>
/// Despesa de frota de uma viatura — parte do agregado <see cref="Vehicle"/>
/// (`modules/fleet.md` §Possui). Combustível, portagens, estacionamento —
/// exactamente as três categorias que `docs/rivo-suite-descricao-modulos.md`
/// nomeia, nada além delas.
///
/// <para>
/// Nasce sempre por <see cref="Vehicle.RegisterExpense"/> — nunca
/// directamente, por isso o construtor é <c>internal</c>. **Nunca se altera
/// nem se elimina depois de criada** (BR-9, BR-14): é o registo do que se
/// gastou, mesma disciplina de <c>StockMovement</c> em `inventory`.
/// </para>
///
/// <para>
/// <strong>Não posta no razão</strong> (`modules/fleet.md` §Não pode) — é o
/// facto operacional que fica aqui; publicar para `finance` é decisão em
/// aberto (`state/pending-decisions.md` — "tempo real ou em lote?", mesma
/// que manteve Custos de fora da Alocação de Recursos em `projects`,
/// 2026-08-31). Sem campo de moeda, de propósito: é sempre AOA, mesma
/// simplificação de `NetSalary` em `payroll` — não há caso de uso real de
/// despesa de frota em moeda estrangeira a pedir o contrário.
/// </para>
/// </summary>
public sealed class FleetExpense
{
    internal FleetExpense(
        Guid id, Guid vehicleId, FleetExpenseCategory category, decimal amount, DateOnly occurredOn, string? description)
    {
        Id = id;
        VehicleId = vehicleId;
        Category = category;
        Amount = amount;
        OccurredOn = occurredOn;
        Description = description;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private FleetExpense()
    {
    }

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public FleetExpenseCategory Category { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    /// <summary>Detalhe livre e opcional — o posto, a via, o local.</summary>
    public string? Description { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }
}

public enum FleetExpenseCategory
{
    Fuel,
    Toll,
    Parking,
}
