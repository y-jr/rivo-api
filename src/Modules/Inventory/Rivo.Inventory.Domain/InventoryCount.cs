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

    /// <summary>
    /// O processo em `approval` que decide esta contagem. Nulo quando a
    /// contagem não precisou de governança — porque não tinha divergências, ou
    /// porque nenhuma alçada configurada cobria o valor em causa (ADR-064).
    ///
    /// <para>
    /// Mesmo desenho de <c>PayrollRun.ApprovalRequestId</c>: um ponteiro, e não
    /// uma cópia do estado da decisão. Quem quer saber se já foi decidida
    /// pergunta a `approval`; este lado nunca guarda a resposta dele.
    /// </para>
    /// </summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>
    /// Valor absoluto da divergência que foi submetida a decisão — a soma de
    /// |variância| × custo médio de cada item. Congelado na submissão, porque é
    /// com este número que a alçada foi escolhida e é por ele que a decisão será
    /// lida depois.
    /// </summary>
    public decimal? SubmittedVarianceValue { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

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
    ///
    /// <para>
    /// Fecha-se a partir de <see cref="InventoryCountStatus.Open"/> — quando não
    /// houve divergências ou nenhuma alçada as cobria — ou de
    /// <see cref="InventoryCountStatus.PendingApproval"/>, depois de aprovada
    /// (ADR-064). Nos dois casos é este acto que autoriza os ajustes.
    /// </para>
    /// </summary>
    public void Close()
    {
        if (Status is not (InventoryCountStatus.Open or InventoryCountStatus.PendingApproval))
        {
            throw new InvalidOperationException($"Não é possível fechar: a contagem já está {Status}.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Uma contagem sem nenhuma linha não tem o que confirmar.");
        }

        Status = InventoryCountStatus.Closed;
    }

    /// <summary>
    /// Marca a contagem como submetida a decisão, retendo os ajustes até
    /// alguém decidir (ADR-064).
    ///
    /// <para>
    /// <strong>Nada foi corrigido no stock quando isto acontece.</strong> É a
    /// diferença que a governança existe para impor: uma divergência de
    /// inventário acima da alçada é uma perda a explicar, não um número a
    /// arrumar em silêncio.
    /// </para>
    /// </summary>
    public void MarkSubmitted(Guid approvalRequestId, decimal varianceValue, DateTimeOffset submittedAt)
    {
        EnsureOpen("submeter a decisão");

        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("A submissão precisa do processo de aprovação.", nameof(approvalRequestId));
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Uma contagem sem nenhuma linha não tem o que aprovar.");
        }

        Status = InventoryCountStatus.PendingApproval;
        ApprovalRequestId = approvalRequestId;
        SubmittedVarianceValue = varianceValue;
        SubmittedAt = submittedAt;
    }

    /// <summary>
    /// A decisão foi recusada: a contagem fica por aplicar, e é facto histórico
    /// (BR-14).
    ///
    /// <para>
    /// <strong>Não volta a Aberta, e é deliberado.</strong> Reabrir deixaria
    /// alguém acrescentar linhas a uma sessão de contagem física que já
    /// terminou, e apresentar ao aprovador seguinte números diferentes dos que o
    /// primeiro recusou. Quem quiser tentar de novo conta outra vez — que é o
    /// que se faz quando uma contagem é posta em causa.
    /// </para>
    /// </summary>
    public void MarkRefused(DateTimeOffset settledAt)
    {
        if (Status is not InventoryCountStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Só uma contagem pendente de decisão se recusa. Esta está {Status}.");
        }

        Status = InventoryCountStatus.Refused;
        SettledAt = settledAt;
    }

    /// <summary>Registada a data em que a decisão aprovada foi aplicada.</summary>
    public void MarkSettled(DateTimeOffset settledAt) => SettledAt = settledAt;

    /// <summary>
    /// Divergências que geram correcção de stock, da maior falta para a maior
    /// sobra — a ordem em que alguém as quer ler quando há muitas.
    /// </summary>
    public IReadOnlyList<InventoryCountLine> LinesWithVariance =>
        [.. _lines.Where(l => l.Variance != 0).OrderBy(l => l.Variance)];

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

    /// <summary>
    /// Submetida a decisão, com os ajustes **retidos** (ADR-064). O stock ainda
    /// não mudou, e é isso que distingue este estado de <see cref="Closed"/>.
    /// </summary>
    PendingApproval,

    Closed,

    /// <summary>
    /// A decisão recusou a divergência. A contagem não se aplica e não reabre —
    /// quem quiser tentar de novo conta outra vez.
    /// </summary>
    Refused,

    Cancelled,
}
