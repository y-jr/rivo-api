namespace Rivo.Procurement.Domain;

/// <summary>
/// Recepção de Mercadoria — o registo de que o que foi encomendado chegou.
///
/// <para>
/// <strong>É o terceiro lado do 3-way match</strong>, e o único que nasce de um
/// facto físico em vez de um documento: alguém abriu a caixa e contou. Os
/// outros dois são a Ordem de Compra e a factura do fornecedor, que é de
/// `finance`.
/// </para>
///
/// <para>
/// <strong>Não gere stock, e é fronteira explícita.</strong>
/// `modules/procurement.md` proíbe-o: níveis e valorização são de `inventory`, e
/// `procurement` publica o facto da recepção. Enquanto `inventory` não existir,
/// o facto fica registado e ninguém o consome — que é melhor do que este módulo
/// começar a contar existências e nunca mais largar o assunto.
/// </para>
///
/// <para>
/// <strong>Cancelar uma recepção é corrigir um engano de registo</strong> — a
/// guia lançada na ordem errada, a contagem mal feita —, e não devolver
/// mercadoria ao fornecedor. A devolução é outro facto: sai material que
/// entrou, e do lado do dinheiro dá nota de crédito. Não existe, e não se
/// finge que este cancelamento a cobre.
/// </para>
/// </summary>
public sealed class GoodsReceipt
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt(
        Guid id,
        Guid purchaseOrderId,
        DateOnly receivedOn,
        Guid receivedByEmployeeId,
        string? deliveryNote)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        ReceivedOn = receivedOn;
        ReceivedByEmployeeId = receivedByEmployeeId;
        DeliveryNote = deliveryNote;
        Status = GoodsReceiptStatus.Registered;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private GoodsReceipt()
    {
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    /// <summary>
    /// Quem recebeu, como Colaborador de `hr` (ADR-010).
    ///
    /// <para>
    /// <strong>Obrigatório, e é o que dá valor ao registo.</strong> Uma
    /// divergência entre o encomendado e o recebido é uma conversa com alguém,
    /// e sem nome não há com quem a ter. É também metade da segregação que o
    /// 3-way match precisa: quem recebe não é quem encomenda.
    /// </para>
    /// </summary>
    public Guid ReceivedByEmployeeId { get; private set; }

    /// <summary>
    /// Referência do documento que veio com a mercadoria — a guia de remessa.
    ///
    /// <para>
    /// <strong>Não está em `docs`, e é inferência.</strong> Fica opcional e em
    /// texto livre: é o número do documento do <em>fornecedor</em>, e o Rivo não
    /// tem como lhe impor formato. Sem ele, uma recepção não se reconcilia com o
    /// papel que ficou no arquivo.
    /// </para>
    /// </summary>
    public string? DeliveryNote { get; private set; }

    public GoodsReceiptStatus Status { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Concorrência optimista (ADR-025). O domínio nunca lhe toca.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Regista a recepção.
    ///
    /// <para>
    /// <strong>O agregado não sabe o que foi encomendado.</strong> Não vê a
    /// ordem, não vê as outras recepções contra ela, e por isso não pode
    /// verificar se o que chegou cabe no que se pediu. Quem o faz é a camada
    /// Application, que vê o conjunto — e é lá que a regra está escrita.
    /// </para>
    /// </summary>
    public static GoodsReceipt Register(
        Guid purchaseOrderId,
        DateOnly receivedOn,
        Guid receivedByEmployeeId,
        string? deliveryNote)
    {
        if (purchaseOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma recepção é sempre contra uma ordem de compra — não se recebe o que não se encomendou.",
                nameof(purchaseOrderId));
        }

        if (receivedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma recepção precisa de quem recebeu: uma divergência é uma conversa com alguém.",
                nameof(receivedByEmployeeId));
        }

        return new GoodsReceipt(
            Guid.CreateVersion7(),
            purchaseOrderId,
            receivedOn,
            receivedByEmployeeId,
            string.IsNullOrWhiteSpace(deliveryNote) ? null : deliveryNote.Trim());
    }

    /// <summary>
    /// Acrescenta a contagem de uma linha da ordem.
    /// </summary>
    /// <param name="purchaseOrderLineId">
    /// A linha da ordem que esta contagem satisfaz. <strong>É por ela que o
    /// 3-way match compara</strong> — sem a ligação linha a linha, receber duas
    /// unidades de uma coisa e nenhuma de outra somaria certo no total e estaria
    /// errado em tudo o resto.
    /// </param>
    public GoodsReceiptLine AddLine(Guid purchaseOrderLineId, decimal quantityReceived)
    {
        if (Status is not GoodsReceiptStatus.Registered)
        {
            throw new InvalidOperationException(
                $"Uma recepção em {Status} não se altera.");
        }

        if (_lines.Any(l => l.PurchaseOrderLineId == purchaseOrderLineId))
        {
            throw new InvalidOperationException(
                "A mesma linha da ordem já foi contada nesta recepção. " +
                "Duas contagens da mesma coisa no mesmo acto são um engano, não uma entrega parcial.");
        }

        var linha = new GoodsReceiptLine(
            Guid.CreateVersion7(), Id, purchaseOrderLineId, quantityReceived);

        _lines.Add(linha);

        return linha;
    }

    /// <summary>
    /// Anula a recepção — o registo estava errado.
    ///
    /// <para>
    /// <strong>Nunca eliminar</strong> (BR-14). Uma recepção anulada é um erro
    /// que foi cometido, e o registo de o ter sido é a parte que interessa a
    /// quem audita.
    /// </para>
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is GoodsReceiptStatus.Cancelled)
        {
            throw new InvalidOperationException("A recepção já está anulada.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Anular uma recepção precisa de razão: alguém contou mal, e isso tem de ficar escrito.",
                nameof(reason));
        }

        CancellationReason = reason.Trim();
        CancelledAt = at;
        Status = GoodsReceiptStatus.Cancelled;
    }
}

public enum GoodsReceiptStatus
{
    /// <summary>Registada e a contar para o que foi recebido.</summary>
    Registered,

    /// <summary>Anulada por engano de registo. Deixa de contar.</summary>
    Cancelled,
}

/// <summary>
/// Contagem de uma linha da ordem: quanto chegou, daquilo que se pediu.
/// </summary>
public sealed class GoodsReceiptLine
{
    internal GoodsReceiptLine(
        Guid id,
        Guid goodsReceiptId,
        Guid purchaseOrderLineId,
        decimal quantityReceived)
    {
        if (purchaseOrderLineId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma contagem é sempre de uma linha da ordem.", nameof(purchaseOrderLineId));
        }

        if (quantityReceived <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityReceived), quantityReceived,
                "A quantidade recebida tem de ser positiva. Não receber nada não é uma recepção — " +
                "é a ausência dela, e não se regista.");
        }

        Id = id;
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        QuantityReceived = quantityReceived;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private GoodsReceiptLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid GoodsReceiptId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public decimal QuantityReceived { get; private set; }
}
