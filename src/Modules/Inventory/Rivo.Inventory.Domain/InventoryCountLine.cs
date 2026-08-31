namespace Rivo.Inventory.Domain;

/// <summary>
/// Uma linha de <see cref="InventoryCount"/> — um item contado, com a
/// quantidade que o sistema esperava e a que se encontrou fisicamente.
///
/// <para>
/// Nasce sempre por <see cref="InventoryCount.AddLine"/> — nunca
/// directamente, por isso o construtor é <c>internal</c>. **Nunca se altera
/// nem se elimina depois de criada** (BR-9, BR-14), mesma disciplina de
/// <see cref="StockMovement"/>: é o registo do que se contou.
/// </para>
/// </summary>
public sealed class InventoryCountLine
{
    internal InventoryCountLine(Guid id, Guid countId, Guid itemId, decimal expectedQuantity, decimal countedQuantity)
    {
        Id = id;
        CountId = countId;
        ItemId = itemId;
        ExpectedQuantity = expectedQuantity;
        CountedQuantity = countedQuantity;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private InventoryCountLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid CountId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>
    /// A quantidade que <see cref="InventoryItem.QuantityOnHandAt"/> devolvia
    /// no momento em que esta linha nasceu — congelada, nunca recalculada.
    /// </summary>
    public decimal ExpectedQuantity { get; private set; }

    /// <summary>A quantidade fisicamente encontrada.</summary>
    public decimal CountedQuantity { get; private set; }

    /// <summary>Diferença entre o contado e o esperado. Positiva: sobra. Negativa: falta.</summary>
    public decimal Variance => CountedQuantity - ExpectedQuantity;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }
}
