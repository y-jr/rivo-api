namespace Rivo.Procurement.Domain;

/// <summary>
/// Ordem de Compra — o segundo elo da cadeia, e o primeiro que sai da empresa.
///
/// <para>
/// <strong>Só nasce de requisição aprovada</strong>, e é regra explícita de
/// `modules/procurement.md`. Não há ordem avulsa: encomendar sem decisão
/// registada é exactamente o que a governança existe para impedir, e o fluxo
/// leve de despesa eventual — que `docs` classifica como expansão futura deste
/// módulo — não existe.
/// </para>
///
/// <para>
/// <strong>As linhas não são cópia das da requisição.</strong> A requisição diz
/// o que se quer e por quanto se <em>estima</em>; a ordem diz o que se encomenda
/// e por quanto se <em>acordou</em>. Entre as duas houve cotação, e é essa a
/// razão de o passo existir — copiar os preços estimados faria da cotação um
/// campo decorativo.
/// </para>
///
/// <para>
/// <strong>Não tem número próprio, e é lacuna assumida.</strong> Uma ordem que
/// sai para o fornecedor precisa de uma referência que ele possa citar de volta,
/// e escolher o formato — prefixo, reinício anual, se admite saltos — é decisão
/// de negócio que não está em fonte nenhuma deste repositório. Fica o
/// identificador, e o número quando houver quem o decida.
/// </para>
/// </summary>
public sealed class PurchaseOrder
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(
        Guid id,
        Guid requisitionId,
        Guid supplierId,
        string currency,
        DateOnly issuedOn,
        DateOnly? expectedOn)
    {
        Id = id;
        RequisitionId = requisitionId;
        SupplierId = supplierId;
        Currency = currency;
        IssuedOn = issuedOn;
        ExpectedOn = expectedOn;
        Status = PurchaseOrderStatus.Issued;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PurchaseOrder()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// A requisição que a autorizou. Obrigatória — é o que liga a encomenda à
    /// decisão que a permitiu, e sem ela não há como saber quem a aprovou.
    /// </summary>
    public Guid RequisitionId { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>ISO 4217. Tem de coincidir com a da requisição.</summary>
    public string Currency { get; private set; }

    public DateOnly IssuedOn { get; private set; }

    /// <summary>Data de entrega esperada. Opcional: nem sempre está acordada.</summary>
    public DateOnly? ExpectedOn { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Valor acordado. Ao contrário do da requisição, este é compromisso: é o
    /// que se vai dever ao fornecedor quando a factura chegar.
    /// </summary>
    public decimal Total => _lines.Sum(line => line.LineTotal);

    /// <summary>
    /// Concorrência optimista (ADR-025). O domínio nunca lhe toca.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Emite a ordem.
    ///
    /// <para>
    /// <strong>O agregado não verifica se a requisição está aprovada</strong>, e
    /// não é esquecimento: não a vê. Recebe o identificador, e quem confirma o
    /// estado — e o do fornecedor, e o total já encomendado contra a mesma
    /// requisição — é a camada Application, que vê o conjunto. Fingir aqui uma
    /// garantia que o agregado não pode dar era a pior das hipóteses.
    /// </para>
    /// </summary>
    public static PurchaseOrder Issue(
        Guid requisitionId,
        Guid supplierId,
        string currency,
        DateOnly issuedOn,
        DateOnly? expectedOn)
    {
        if (requisitionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma ordem de compra nasce de uma requisição aprovada — não há ordem avulsa.",
                nameof(requisitionId));
        }

        if (supplierId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uma ordem de compra precisa de fornecedor.", nameof(supplierId));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "A moeda é o código ISO 4217, com três letras (`AOA` para o kwanza).",
                nameof(currency));
        }

        if (expectedOn is { } entrega && entrega < issuedOn)
        {
            throw new ArgumentException(
                "A entrega esperada não pode ser anterior à emissão da ordem.",
                nameof(expectedOn));
        }

        return new PurchaseOrder(
            Guid.CreateVersion7(),
            requisitionId,
            supplierId,
            currency.Trim().ToUpperInvariant(),
            issuedOn,
            expectedOn);
    }

    /// <summary>
    /// Acrescenta uma linha.
    ///
    /// <para>
    /// <strong>Só antes de a ordem sair.</strong> Na prática, isso é o intervalo
    /// entre a emissão e a gravação — a ordem nasce completa, e alterá-la depois
    /// seria mudar uma encomenda que o fornecedor já tem. O que existe para isso
    /// é cancelar e emitir outra, que deixa rasto das duas.
    /// </para>
    /// </summary>
    public PurchaseOrderLine AddLine(string description, decimal quantity, decimal unitPrice)
    {
        if (Status is not PurchaseOrderStatus.Issued)
        {
            throw new InvalidOperationException(
                $"Uma ordem em {Status} não se altera. Cancele e emita outra.");
        }

        var linha = new PurchaseOrderLine(Guid.CreateVersion7(), Id, description, quantity, unitPrice);

        _lines.Add(linha);

        return linha;
    }

    /// <summary>
    /// Cancela a ordem.
    ///
    /// <para>
    /// <strong>Nunca eliminar</strong> (BR-14). Uma ordem cancelada foi enviada
    /// a alguém, e o fornecedor pode ter agido sobre ela — apagá-la deixaria a
    /// empresa sem registo de uma encomenda que existiu.
    /// </para>
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is PurchaseOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A ordem já está cancelada.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Um cancelamento precisa de razão: alguém do outro lado vai perguntar porquê.",
                nameof(reason));
        }

        CancellationReason = reason.Trim();
        CancelledAt = at;
        Status = PurchaseOrderStatus.Cancelled;
    }
}

public enum PurchaseOrderStatus
{
    /// <summary>Emitida e em vigor. É o estado em que a ordem passa a vida.</summary>
    Issued,

    Cancelled,
}

/// <summary>
/// Linha de ordem de compra: o que se encomenda, quanto, e ao preço acordado.
///
/// <para>
/// <strong>A quantidade é o que a recepção vai comparar.</strong> É contra ela
/// que o 3-way match verifica o que chegou — e por isso é o campo desta linha
/// que menos pode estar errado.
/// </para>
/// </summary>
public sealed class PurchaseOrderLine
{
    internal PurchaseOrderLine(
        Guid id,
        Guid purchaseOrderId,
        string description,
        decimal quantity,
        decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Uma linha precisa de descrição do que se encomenda.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A quantidade encomendada tem de ser positiva.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice), unitPrice,
                "O preço acordado não pode ser negativo.");
        }

        Id = id;
        PurchaseOrderId = purchaseOrderId;
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PurchaseOrderLine()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Preço acordado com o fornecedor, e não o estimado na requisição.</summary>
    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
