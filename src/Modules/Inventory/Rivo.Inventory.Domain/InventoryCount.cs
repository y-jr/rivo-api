namespace Rivo.Inventory.Domain;

/// <summary>
/// Contagem de inventário — agregado raiz de `inventory` (`modules/inventory.md`
/// §Possui). Inventariação periódica de um armazém: abre-se, acumula uma
/// linha por item contado, e fecha-se — o fecho é o que gera as correcções
/// de stock, nunca a linha isolada.
///
/// <para>
/// <strong>Âmbito é sempre um armazém.</strong> Contar é um acto físico, num
/// local — não faz sentido uma contagem que atravesse vários armazéns.
/// </para>
///
/// <para>
/// <strong>Não é o mesmo agregado de <see cref="InventoryItem"/>.</strong> Uma
/// contagem cobre muitos itens; não pode nascer dentro de um único item sem
/// fragmentar a sessão de contagem em pedaços que não se abrem nem fecham
/// juntos. Cada linha refere o item só por <c>ItemId</c> (ADR-010, aqui
/// dentro do mesmo módulo) — a correcção efectiva acontece no agregado
/// <see cref="InventoryItem"/>, orquestrada pela Application ao fechar
/// (<c>CloseInventoryCount</c>), nunca por travessia directa de agregado.
/// </para>
///
/// <para>
/// <strong>A quantidade esperada de cada linha fica congelada no momento em
/// que a linha é acrescentada</strong> — não recalculada no fecho. Uma
/// contagem existe para comparar "o que o sistema achava quando se contou"
/// com "o que se encontrou fisicamente"; recalcular no fecho absorveria em
/// silêncio qualquer movimento acontecido durante a contagem, escondendo
/// exactamente a divergência que a contagem existe para apanhar. Se algo
/// mudou entretanto, o ajuste gerado no fecho pode não ser exacto — é um
/// risco aceite de qualquer contagem física contra um sistema que continua
/// a mover-se, não um defeito a resolver aqui com bloqueio.
/// </para>
/// </summary>
public sealed class InventoryCount
{
    private readonly List<InventoryCountLine> _lines = [];

    private InventoryCount(Guid id, Guid warehouseId, DateOnly occurredOn)
    {
        Id = id;
        WarehouseId = warehouseId;
        OccurredOn = occurredOn;
        Status = InventoryCountStatus.Open;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private InventoryCount()
    {
    }

    public Guid Id { get; private set; }

    public Guid WarehouseId { get; private set; }

    /// <summary>Data em que a contagem física aconteceu.</summary>
    public DateOnly OccurredOn { get; private set; }

    public InventoryCountStatus Status { get; private set; }

    /// <summary>Motivo do cancelamento — só preenchido quando <see cref="Status"/> é <see cref="InventoryCountStatus.Cancelled"/>.</summary>
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<InventoryCountLine> Lines => _lines;

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static InventoryCount Open(Guid warehouseId, DateOnly occurredOn)
    {
        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Uma contagem precisa de armazém.", nameof(warehouseId));
        }

        return new InventoryCount(Guid.CreateVersion7(), warehouseId, occurredOn);
    }

    /// <summary>
    /// Acrescenta uma linha contada. <paramref name="expectedQuantity"/> é a
    /// quantidade que o sistema tinha nesse armazém no momento em que esta
    /// linha nasce — fornecida pela Application (lida de
    /// <see cref="InventoryItem.QuantityOnHandAt"/>), nunca recalculada por
    /// este agregado, que não tem acesso a outros itens.
    /// </summary>
    public InventoryCountLine AddLine(Guid itemId, decimal countedQuantity, decimal expectedQuantity)
    {
        EnsureOpen("acrescentar uma linha");

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("Uma linha de contagem precisa de item.", nameof(itemId));
        }

        if (countedQuantity < 0)
        {
            throw new ArgumentException("A quantidade contada não pode ser negativa.", nameof(countedQuantity));
        }

        if (_lines.Any(l => l.ItemId == itemId))
        {
            throw new InvalidOperationException(
                "Este item já tem uma linha nesta contagem — não se conta duas vezes na mesma sessão.");
        }

        var linha = new InventoryCountLine(Guid.CreateVersion7(), Id, itemId, expectedQuantity, countedQuantity);
        _lines.Add(linha);

        return linha;
    }

    /// <summary>
    /// Fecha a contagem. Só transita o estado — quem gera as correcções de
    /// stock a partir de <see cref="Lines"/> é a Application
    /// (<c>CloseInventoryCount</c>), porque isso exige tocar no agregado
    /// <see cref="InventoryItem"/> de cada linha, fora do alcance deste
    /// agregado.
    /// </summary>
    public void Close()
    {
        EnsureOpen("fechar");

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Uma contagem sem nenhuma linha não tem o que confirmar.");
        }

        Status = InventoryCountStatus.Closed;
    }

    /// <summary>Cancela uma contagem aberta por engano. Exige motivo — mesma disciplina de um Ajuste sem explicação.</summary>
    public void Cancel(string reason)
    {
        EnsureOpen("cancelar");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancelar uma contagem precisa de motivo.", nameof(reason));
        }

        Status = InventoryCountStatus.Cancelled;
        CancellationReason = reason.Trim();
    }

    private void EnsureOpen(string acto)
    {
        if (Status is not InventoryCountStatus.Open)
        {
            throw new InvalidOperationException($"Não é possível {acto}: a contagem já está {Status}.");
        }
    }
}

public enum InventoryCountStatus
{
    Open,
    Closed,
    Cancelled,
}
