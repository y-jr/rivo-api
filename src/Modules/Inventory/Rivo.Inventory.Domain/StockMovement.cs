namespace Rivo.Inventory.Domain;

/// <summary>
/// Movimento de stock — parte do agregado <see cref="InventoryItem"/>
/// (`modules/inventory.md` §Possui).
///
/// <para>
/// Nasce sempre por um dos métodos de <see cref="InventoryItem"/>
/// (<see cref="InventoryItem.RegisterReceipt"/>,
/// <see cref="InventoryItem.RegisterIssue"/>,
/// <see cref="InventoryItem.RegisterAdjustment"/>) — nunca directamente, por
/// isso o construtor é <c>internal</c>. **Nunca se altera nem se elimina
/// depois de criado** (BR-9, BR-14): é o registo do que aconteceu, e é a
/// soma dos movimentos que define <see cref="InventoryItem.QuantityOnHand"/>,
/// nunca o inverso.
/// </para>
///
/// <para>
/// <strong>Transferência entre armazéns fica de fora</strong> — não há ainda
/// Armazém (`modules/inventory.md` §Perguntas em aberto). Os três tipos aqui
/// são os que fazem sentido sem essa peça.
/// </para>
/// </summary>
public sealed class StockMovement
{
    internal StockMovement(
        Guid id,
        Guid itemId,
        StockMovementType type,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        DateTimeOffset recordedAt)
    {
        Id = id;
        ItemId = itemId;
        Type = type;
        Quantity = quantity;
        Reason = reason;
        OccurredOn = occurredOn;
        RecordedAt = recordedAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private StockMovement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ItemId { get; private set; }

    public StockMovementType Type { get; private set; }

    /// <summary>
    /// A variação aplicada à quantidade em mão, com sinal: positiva em
    /// <see cref="StockMovementType.Receipt"/>, negativa em
    /// <see cref="StockMovementType.Issue"/>, qualquer sinal (nunca zero) em
    /// <see cref="StockMovementType.Adjustment"/>.
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// Obrigatório em <see cref="StockMovementType.Adjustment"/> — uma
    /// correcção sem motivo é exactamente o que este campo existe para
    /// impedir. Opcional nos outros dois.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>Data do facto — quando a mercadoria chegou, saiu, ou a contagem se fez.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>Quando o registo foi gravado no sistema. Metadado, não facto de negócio.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }
}

public enum StockMovementType
{
    Receipt,
    Issue,
    Adjustment,
}
