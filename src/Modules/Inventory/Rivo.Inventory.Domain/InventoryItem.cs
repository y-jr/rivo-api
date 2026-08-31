namespace Rivo.Inventory.Domain;

/// <summary>
/// Item de inventário — agregado raiz de `inventory` (ver `modules/inventory.md`).
///
/// <para>
/// <strong>Movimento vive aqui dentro</strong> (§Possui): nasce sempre por
/// este agregado (<see cref="RegisterReceipt"/>, <see cref="RegisterIssue"/>,
/// <see cref="RegisterAdjustment"/>, <see cref="Transfer"/>), e é a soma dos
/// movimentos que define <see cref="QuantityOnHand"/> e
/// <see cref="QuantityOnHandAt"/> — nunca o inverso, e nunca editável
/// directamente. Contagem e valorização de stock continuam por fazer.
/// </para>
///
/// <para>
/// <strong>2026-08-31 — retrofit de Armazém.</strong> Todo o movimento passou
/// a exigir um <c>WarehouseId</c> (`modules/inventory.md` — decisão de
/// retrofit, não de convivência com o desenho antigo).
/// <see cref="QuantityOnHand"/> mantém-se como o total agregado, por
/// conveniência de leitura; <see cref="QuantityOnHandAt"/> dá a quantidade
/// por armazém. <see cref="Warehouse"/> é um agregado raiz próprio, referenciado
/// só por identificador — nunca uma posse deste agregado.
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
    /// Quantidade em mão, somada em todos os armazéns. É a soma assinada de
    /// <see cref="Movements"/> — mantida aqui por conveniência de leitura,
    /// nunca escrita directamente. Para a quantidade num armazém concreto,
    /// ver <see cref="QuantityOnHandAt"/>.
    /// </summary>
    public decimal QuantityOnHand { get; private set; }

    public InventoryItemStatus Status { get; private set; }

    public IReadOnlyList<StockMovement> Movements => _movements;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    /// <summary>Quantidade em mão num armazém concreto — soma assinada dos movimentos desse armazém.</summary>
    public decimal QuantityOnHandAt(Guid warehouseId) =>
        _movements.Where(m => m.WarehouseId == warehouseId).Sum(m => m.Quantity);

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

    /// <summary>Entrada de mercadoria num armazém. Aumenta <see cref="QuantityOnHand"/> e a quantidade desse armazém.</summary>
    public StockMovement RegisterReceipt(
        Guid warehouseId, decimal quantity, string? reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar uma recepção");
        EnsureWarehouse(warehouseId);

        if (quantity <= 0)
        {
            throw new ArgumentException("A quantidade recebida tem de ser positiva.", nameof(quantity));
        }

        return AddMovement(StockMovementType.Receipt, warehouseId, quantity, reason, occurredOn, recordedAt);
    }

    /// <summary>
    /// Saída de mercadoria de um armazém. Reduz <see cref="QuantityOnHand"/> e a quantidade desse armazém.
    ///
    /// <para>
    /// <strong>Nunca abaixo de zero nesse armazém</strong> — sair mais do que
    /// há em mão nesse armazém é recusado, não truncado, e não se compensa
    /// com o que há noutro armazém. Um valor truncado ou emprestado de outro
    /// armazém esconderia a divergência em vez de a mostrar.
    /// </para>
    /// </summary>
    public StockMovement RegisterIssue(
        Guid warehouseId, decimal quantity, string? reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar uma saída");
        EnsureWarehouse(warehouseId);

        if (quantity <= 0)
        {
            throw new ArgumentException("A quantidade a sair tem de ser positiva.", nameof(quantity));
        }

        var disponivel = QuantityOnHandAt(warehouseId);

        if (quantity > disponivel)
        {
            throw new InvalidOperationException(
                $"Não há quantidade suficiente em mão nesse armazém: {disponivel} disponível, {quantity} pedido.");
        }

        return AddMovement(StockMovementType.Issue, warehouseId, -quantity, reason, occurredOn, recordedAt);
    }

    /// <summary>
    /// Correcção de contagem num armazém, para cima ou para baixo. **Exige
    /// motivo** — uma correcção sem explicação é exactamente o que este
    /// método existe para impedir.
    /// </summary>
    public StockMovement RegisterAdjustment(
        Guid warehouseId, decimal quantityDelta, string reason, DateOnly occurredOn, DateTimeOffset recordedAt)
    {
        EnsureActive("registar um ajuste");
        EnsureWarehouse(warehouseId);

        if (quantityDelta == 0)
        {
            throw new ArgumentException("Um ajuste sem variação não altera nada.", nameof(quantityDelta));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Um ajuste precisa de motivo.", nameof(reason));
        }

        var disponivel = QuantityOnHandAt(warehouseId);

        if (disponivel + quantityDelta < 0)
        {
            throw new InvalidOperationException(
                $"Este ajuste puxaria a quantidade em mão desse armazém para negativo: {disponivel} corrigido por {quantityDelta}.");
        }

        return AddMovement(StockMovementType.Adjustment, warehouseId, quantityDelta, reason.Trim(), occurredOn, recordedAt);
    }

    /// <summary>
    /// Transferência entre dois armazéns do mesmo item, num só passo — sem
    /// estado intermédio "em trânsito" (decisão confirmada: transferência
    /// atómica). Gera duas pernas ligadas por
    /// <see cref="StockMovement.RelatedWarehouseId"/>: uma saída no armazém de
    /// origem e uma entrada no de destino, na mesma quantidade — por isso
    /// <see cref="QuantityOnHand"/> (o total agregado) nunca muda com uma
    /// transferência, só a distribuição por armazém.
    /// </summary>
    public (StockMovement Out, StockMovement In) Transfer(
        Guid fromWarehouseId,
        Guid toWarehouseId,
        decimal quantity,
        string? reason,
        DateOnly occurredOn,
        DateTimeOffset recordedAt)
    {
        EnsureActive("registar uma transferência");
        EnsureWarehouse(fromWarehouseId);
        EnsureWarehouse(toWarehouseId);

        if (fromWarehouseId == toWarehouseId)
        {
            throw new ArgumentException(
                "Uma transferência exige um armazém de origem diferente do de destino.", nameof(toWarehouseId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException("A quantidade a transferir tem de ser positiva.", nameof(quantity));
        }

        var disponivel = QuantityOnHandAt(fromWarehouseId);

        if (quantity > disponivel)
        {
            throw new InvalidOperationException(
                $"Não há quantidade suficiente no armazém de origem: {disponivel} disponível, {quantity} pedido.");
        }

        var saida = AddMovement(
            StockMovementType.TransferOut, fromWarehouseId, -quantity, reason, occurredOn, recordedAt, toWarehouseId);
        var entrada = AddMovement(
            StockMovementType.TransferIn, toWarehouseId, quantity, reason, occurredOn, recordedAt, fromWarehouseId);

        return (saida, entrada);
    }

    private StockMovement AddMovement(
        StockMovementType type,
        Guid warehouseId,
        decimal signedQuantity,
        string? reason,
        DateOnly occurredOn,
        DateTimeOffset recordedAt,
        Guid? relatedWarehouseId = null)
    {
        var movimento = new StockMovement(
            Guid.CreateVersion7(), Id, type, warehouseId, signedQuantity, reason, occurredOn, recordedAt, relatedWarehouseId);

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

    private static void EnsureWarehouse(Guid warehouseId)
    {
        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Um movimento precisa de armazém.", nameof(warehouseId));
        }
    }
}

public enum InventoryItemStatus
{
    Active,
    Inactive,
}
