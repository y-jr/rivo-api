namespace Rivo.Finance.Domain;

/// <summary>
/// Recibo. Regista dinheiro efectivamente recebido contra facturas de venda.
///
/// <para>
/// <strong>Distinto da factura, e a distinção é o ponto.</strong> Uma factura
/// diz o que é devido; um recibo diz o que foi pago. Confundi-los é o que faz um
/// mapa de dívida mentir — e é o erro que o `docs` regista como
/// `RecebimentoRegistado` ser evento próprio.
/// </para>
///
/// <para>
/// Um recibo pode liquidar <strong>várias facturas</strong>, que é o caso
/// corrente de quem paga um extracto de uma vez. Cada linha diz que quantia foi
/// para que factura, porque sem isso não há como saber o que ficou por receber.
/// </para>
/// </summary>
public sealed class Receipt
{
    private readonly List<ReceiptLine> _lines = [];

    private Receipt(
        Guid id,
        DocumentNumber number,
        DateOnly receivedOn,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        PaymentMethod method,
        string? notes,
        string? fiscalNotice)
    {
        Id = id;
        Number = number;
        ReceivedOn = receivedOn;
        CustomerId = customerId;
        Customer = customer;
        Currency = currency;
        Method = method;
        Notes = notes;
        FiscalNotice = fiscalNotice;
        Status = InvoiceStatus.Normal;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Receipt()
    {
        Number = null!;
        Customer = null!;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public DocumentNumber Number { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public Guid? CustomerId { get; private set; }

    /// <summary>O cliente, congelado como nos outros documentos.</summary>
    public InvoicedParty Customer { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Meio de pagamento do SAF-T (`modules/fiscal.md`).</summary>
    public PaymentMethod Method { get; private set; }

    public string? Notes { get; private set; }

    public string? FiscalNotice { get; private set; }

    public IReadOnlyList<ReceiptLine> Lines => _lines;

    public decimal Total { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    /// <param name="settlements">
    /// Quanto foi para cada factura. As facturas vêm resolvidas de fora — o
    /// agregado não as vê, e é a camada Application que confirma que existem e
    /// que o valor cabe no que falta receber.
    /// </param>
    public static Receipt Register(
        DocumentNumber number,
        DateOnly receivedOn,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        PaymentMethod method,
        IReadOnlyList<NewSettlement> settlements,
        string? notes = null,
        string? fiscalNotice = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(customer);

        if (number.Type is not DocumentType.RG)
        {
            throw new ArgumentException(
                $"Um recibo numera-se em série RG, não {number.Type}.", nameof(number));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217.", nameof(currency));
        }

        if (settlements is null || settlements.Count == 0)
        {
            throw new ArgumentException(
                "Um recibo diz sempre a que factura o dinheiro foi.", nameof(settlements));
        }

        // A mesma factura duas vezes no mesmo recibo é engano de quem chama, e
        // esconderia metade do valor de quem some as linhas por factura.
        if (settlements.Select(s => s.SalesInvoiceId).Distinct().Count() != settlements.Count)
        {
            throw new ArgumentException(
                "A mesma factura aparece mais de uma vez no recibo.", nameof(settlements));
        }

        var recibo = new Receipt(
            Guid.CreateVersion7(),
            number,
            receivedOn,
            customerId,
            customer,
            currency.Trim().ToUpperInvariant(),
            method,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            string.IsNullOrWhiteSpace(fiscalNotice) ? null : fiscalNotice.Trim());

        var ordem = 1;

        foreach (var liquidacao in settlements)
        {
            recibo._lines.Add(ReceiptLine.Create(ordem++, liquidacao));
        }

        recibo.Total = recibo._lines.Sum(line => line.Amount);

        return recibo;
    }

    /// <summary>
    /// Anula o recibo — o estorno de um recebimento.
    ///
    /// <para>
    /// Não elimina (BR-14). A quantia deixa de contar para o saldo das facturas
    /// que liquidava, e elas voltam a estar em dívida. É o que acontece quando
    /// um cheque volta.
    /// </para>
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"O recibo {Number.Formatted} já está anulado desde {CancelledAt:yyyy-MM-dd}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Estornar um recebimento exige motivo — a dívida volta a existir e alguém " +
                "tem de saber porquê.",
                nameof(reason));
        }

        Status = InvoiceStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = at;
    }
}

/// <param name="Amount">Quantia que foi para esta factura, na moeda do recibo.</param>
public sealed record NewSettlement(Guid SalesInvoiceId, string InvoiceNumber, decimal Amount);

/// <summary>
/// Uma liquidação: que quantia deste recibo foi para que factura. Imutável.
/// </summary>
public sealed class ReceiptLine
{
    private ReceiptLine(
        Guid id,
        int lineNumber,
        Guid salesInvoiceId,
        string invoiceNumber,
        decimal amount)
    {
        Id = id;
        LineNumber = lineNumber;
        SalesInvoiceId = salesInvoiceId;
        InvoiceNumber = invoiceNumber;
        Amount = amount;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private ReceiptLine() => InvoiceNumber = string.Empty;

    public Guid Id { get; private set; }

    public Guid ReceiptId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid SalesInvoiceId { get; private set; }

    /// <summary>
    /// O número da factura liquidada, congelado. O SAF-T exige a referência
    /// textual no documento; o identificador serve para navegar.
    /// </summary>
    public string InvoiceNumber { get; private set; }

    public decimal Amount { get; private set; }

    internal static ReceiptLine Create(int lineNumber, NewSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        if (settlement.SalesInvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Uma liquidação aponta sempre a uma factura.", nameof(settlement));
        }

        // Zero não liquida nada, e negativo seria um pagamento ao contrário —
        // isso é uma nota de crédito, não um recibo.
        if (settlement.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settlement), settlement.Amount,
                "A quantia recebida é maior que zero. Devolver dinheiro é nota de crédito.");
        }

        return new ReceiptLine(
            Guid.CreateVersion7(),
            lineNumber,
            settlement.SalesInvoiceId,
            (settlement.InvoiceNumber ?? string.Empty).Trim(),
            Math.Round(settlement.Amount, 2, MidpointRounding.AwayFromZero));
    }
}
