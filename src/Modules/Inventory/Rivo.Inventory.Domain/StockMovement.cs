namespace Rivo.Inventory.Domain;

/// <summary>
/// Movimento de stock — parte do agregado <see cref="InventoryItem"/>
/// (`modules/inventory.md` §Possui).
///
/// <para>
/// Nasce sempre por um dos métodos de <see cref="InventoryItem"/>
/// (<see cref="InventoryItem.RegisterReceipt"/>,
/// <see cref="InventoryItem.RegisterIssue"/>,
/// <see cref="InventoryItem.RegisterAdjustment"/>,
/// <see cref="InventoryItem.Transfer"/>) — nunca directamente, por isso o
/// construtor é <c>internal</c>. **Nunca se altera nem se elimina depois de
/// criado** (BR-9, BR-14): é o registo do que aconteceu, e é a soma dos
/// movimentos por armazém que define
/// <see cref="InventoryItem.QuantityOnHandAt"/>, nunca o inverso.
/// </para>
///
/// <para>
/// <strong>2026-08-31 — retrofit de Armazém.</strong> <see cref="WarehouseId"/>
/// é obrigatório em todos os tipos. <see cref="RelatedWarehouseId"/> só é
/// usado em <see cref="StockMovementType.TransferOut"/> e
/// <see cref="StockMovementType.TransferIn"/> — aponta para o armazém do
/// outro lado da mesma transferência, dando rastreabilidade sem precisar de
/// um identificador de grupo à parte.
/// </para>
///
/// <para>
/// <strong>2026-08-31 — valorização por custo médio ponderado.</strong>
/// <see cref="UnitCost"/> é o custo unitário aplicável a este movimento —
/// congelado no momento em que nasce, nunca recalculado. Na Recepção é o
/// custo de compra indicado por quem regista; nos restantes tipos é o
/// <see cref="InventoryItem.AverageCost"/> corrente no momento (só a
/// Recepção traz informação de custo nova). <see cref="Value"/> é a soma
/// que este movimento representa, com o mesmo sinal de <see cref="Quantity"/>.
/// </para>
/// </summary>
public sealed class StockMovement
{
    internal StockMovement(
        Guid id,
        Guid itemId,
        StockMovementType type,
        Guid warehouseId,
        decimal quantity,
        decimal unitCost,
        string? reason,
        DateOnly occurredOn,
        DateTimeOffset recordedAt,
        Guid? relatedWarehouseId = null)
    {
        Id = id;
        ItemId = itemId;
        Type = type;
        WarehouseId = warehouseId;
        Quantity = quantity;
        UnitCost = unitCost;
        Reason = reason;
        OccurredOn = occurredOn;
        RecordedAt = recordedAt;
        RelatedWarehouseId = relatedWarehouseId;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private StockMovement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ItemId { get; private set; }

    public StockMovementType Type { get; private set; }

    /// <summary>
    /// Armazém onde a variação aconteceu. Em <see cref="StockMovementType.TransferOut"/>
    /// e <see cref="StockMovementType.TransferIn"/>, é o lado de que este
    /// registo é dono — o outro lado está em <see cref="RelatedWarehouseId"/>.
    /// </summary>
    public Guid WarehouseId { get; private set; }

    /// <summary>
    /// O armazém do outro lado de uma transferência. Só preenchido em
    /// <see cref="StockMovementType.TransferOut"/> e
    /// <see cref="StockMovementType.TransferIn"/> — <c>null</c> nos restantes.
    /// </summary>
    public Guid? RelatedWarehouseId { get; private set; }

    /// <summary>
    /// A variação aplicada à quantidade em mão, com sinal: positiva em
    /// <see cref="StockMovementType.Receipt"/> e
    /// <see cref="StockMovementType.TransferIn"/>, negativa em
    /// <see cref="StockMovementType.Issue"/> e
    /// <see cref="StockMovementType.TransferOut"/>, qualquer sinal (nunca
    /// zero) em <see cref="StockMovementType.Adjustment"/>.
    /// </summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// Custo unitário aplicável a este movimento, congelado no momento em
    /// que nasce — ver a nota de valorização acima.
    /// </summary>
    public decimal UnitCost { get; private set; }

    /// <summary>A soma que este movimento representa — <see cref="Quantity"/> vezes <see cref="UnitCost"/>, com o mesmo sinal.</summary>
    public decimal Value => Quantity * UnitCost;

    /// <summary>
    /// Obrigatório em <see cref="StockMovementType.Adjustment"/> — uma
    /// correcção sem motivo é exactamente o que este campo existe para
    /// impedir. Opcional nos restantes.
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

    /// <summary>Perna de saída de uma transferência — ver <see cref="InventoryItem.Transfer"/>.</summary>
    TransferOut,

    /// <summary>Perna de entrada de uma transferência — ver <see cref="InventoryItem.Transfer"/>.</summary>
    TransferIn,
}
