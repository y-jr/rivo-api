namespace Rivo.Inventory.Domain;

/// <summary>
/// Item de inventário — agregado raiz de `inventory` (ver `modules/inventory.md`).
///
/// <para>
/// <strong>Movimento vive aqui dentro</strong> (§Possui): nasce sempre por
/// este agregado (<see cref="RegisterReceipt"/>, <see cref="RegisterIssue"/>,
/// <see cref="RegisterAdjustment"/>), e é a soma dos movimentos que define
/// <see cref="QuantityOnHand"/> — nunca o inverso, e nunca editável
/// directamente. Armazém, Transferência, Contagem e valorização de stock
/// continuam por fazer.
/// </para>
/// </summary>
public sealed class InventoryItem
{
    private readonly List<StockMovement> _movements = [];

    private InventoryItem(Guid id, string sku, string name, string unit)
    {
        Id = id;
        Sku = sku;
        Name = name;
        Unit = unit;
        QuantityOnHand = 0m;
        Status = InventoryItemStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private InventoryItem()
    {
        Sku = string.Empty;
        Name = string.Empty;
        Unit = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Referência do item. Normalizada em maiúsculas.</summary>
    public string Sku { get; private set; }

    public string Name { get; private set; }

    /// <summary>Unidade de medida — "un", "kg", "l", conforme o item.</summary>
    public string Unit { get; private set; }

    /// <summary>
    /// Quantidade em mão. É a soma assinada de <see cref="Movements"/> —
    /// mantida aqui por conveniência de leitura, nunca escrita directamente.
    /// </summary>
    public decimal QuantityOnHand { get; private set; }

    public InventoryItemStatus Status { get; private set; }

    public IReadOnlyList<StockMovement> Movements => _movements;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static InventoryItem Register(string sku, string name, string unit)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Um item precisa de referência (SKU).", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um item precisa de nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("Um item precisa de unidade de medida.", nameof(unit));
        }

        return new InventoryItem(
            Guid.CreateVersion7(), sku.Trim().ToUpperInvariant(), name.Trim(), unit.Trim());
    }

    /// <summary>Desactiva o item. Nunca eliminar — pode estar referenciado por recepções.</summary>
    public void Deactivate()
    {
        Status = InventoryItemStatus.Inactive;
    }

    public void Reactivate()
    {
        Status = InventoryItemStatus.Active;
    }

    /// <summary>Entrada de mercadoria. Aumenta <see cref="QuantityOnHand"/>.</summary>
    public StockMovement RegisterReceipt(decimal quantity, string? reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar uma recepção");

        if (quantity <= 0)
        {
            throw new ArgumentException("A quantidade recebida tem de ser positiva.", nameof(quantity));
        }

        return AddMovement(StockMovementType.Receipt, quantity, reason, occurredOn, recordedAt);
    }

    /// <summary>
    /// Saída de mercadoria. Reduz <see cref="QuantityOnHand"/>.
    ///
    /// <para>
    /// <strong>Nunca abaixo de zero</strong> — sair mais do que há em mão é
    /// recusado, não truncado. Um valor truncado esconderia a divergência em
    /// vez de a mostrar.
    /// </para>
    /// </summary>
    public StockMovement RegisterIssue(decimal quantity, string? reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar uma saída");

        if (quantity <= 0)
        {
            throw new ArgumentException("A quantidade a sair tem de ser positiva.", nameof(quantity));
        }

        if (quantity > QuantityOnHand)
        {
            throw new InvalidOperationException(
                $"Não há quantidade suficiente em mão: {QuantityOnHand} disponível, {quantity} pedido.");
        }

        return AddMovement(StockMovementType.Issue, -quantity, reason, occurredOn, recordedAt);
    }

    /// <summary>
    /// Correcção de contagem, para cima ou para baixo. **Exige motivo** — uma
    /// correcção sem explicação é exactamente o que este método existe para
    /// impedir.
    /// </summary>
    public StockMovement RegisterAdjustment(
        decimal quantityDelta, string reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar um ajuste");

        if (quantityDelta == 0)
        {
            throw new ArgumentException("Um ajuste sem variação não altera nada.", nameof(quantityDelta));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Um ajuste precisa de motivo.", nameof(reason));
        }

        if (QuantityOnHand + quantityDelta < 0)
        {
            throw new InvalidOperationException(
                $"Este ajuste puxaria a quantidade em mão para negativo: {QuantityOnHand} corrigido por {quantityDelta}.");
        }

        return AddMovement(StockMovementType.Adjustment, quantityDelta, reason.Trim(), occurredOn, recordedAt);
    }

    private StockMovement AddMovement(
        StockMovementType type, decimal signedQuantity, string? reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        var movimento = new StockMovement(
            Guid.CreateVersion7(), Id, type, signedQuantity, reason, occurredOn, recordedAt);

        _movements.Add(movimento);
        QuantityOnHand += signedQuantity;

        return movimento;
    }

    private void EnsureActive(string acto)
    {
        if (Status is InventoryItemStatus.Inactive)
        {
            throw new InvalidOperationException($"Não é possível {acto}: o item está inactivo.");
        }
    }
}

public enum InventoryItemStatus
{
    Active,
    Inactive,
}
