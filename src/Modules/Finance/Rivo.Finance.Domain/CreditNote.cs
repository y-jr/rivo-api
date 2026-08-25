namespace Rivo.Finance.Domain;

/// <summary>
/// Nota de crédito. Corrige uma factura de venda já emitida.
///
/// <para>
/// <strong>Existe porque a factura é imutável.</strong> Corrigir um documento
/// emitido não é reescrevê-lo — é emitir outro que o referencia e o reduz. É a
/// mesma razão que faz `Decision` ser imutável em `approval`: um facto
/// histórico corrige-se acrescentando, nunca alterando.
/// </para>
///
/// <para>
/// Referencia <strong>uma</strong> factura. O SAF-T permite referenciar várias
/// numa só nota, e essa forma fica por fazer — declarada em `modules/finance.md`
/// e não escondida aqui.
/// </para>
/// </summary>
public sealed class CreditNote
{
    private readonly List<CreditNoteLine> _lines = [];

    private CreditNote(
        Guid id,
        DocumentNumber number,
        Guid salesInvoiceId,
        string correctedInvoiceNumber,
        DateOnly issuedOn,
        DateOnly taxPointDate,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        string reason,
        string? fiscalNotice)
    {
        Id = id;
        Number = number;
        SalesInvoiceId = salesInvoiceId;
        CorrectedInvoiceNumber = correctedInvoiceNumber;
        IssuedOn = issuedOn;
        TaxPointDate = taxPointDate;
        CustomerId = customerId;
        Customer = customer;
        Currency = currency;
        Reason = reason;
        FiscalNotice = fiscalNotice;
        Status = InvoiceStatus.Normal;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private CreditNote()
    {
        Number = null!;
        Customer = null!;
        Currency = string.Empty;
        Reason = string.Empty;
        CorrectedInvoiceNumber = string.Empty;
    }

    public Guid Id { get; private set; }

    public DocumentNumber Number { get; private set; }

    /// <summary>A factura que esta nota corrige.</summary>
    public Guid SalesInvoiceId { get; private set; }

    /// <summary>
    /// O número da factura corrigida, congelado.
    ///
    /// <para>
    /// Duplica <see cref="SalesInvoiceId"/> de propósito: o SAF-T exige a
    /// <c>Reference</c> textual no documento, e é isso que se lê num papel. O
    /// identificador serve para navegar, o número para provar.
    /// </para>
    /// </summary>
    public string CorrectedInvoiceNumber { get; private set; }

    public DateOnly IssuedOn { get; private set; }

    public DateOnly TaxPointDate { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public Guid? CustomerId { get; private set; }

    /// <summary>
    /// O cliente da factura corrigida, congelado tal como estava **na emissão
    /// dela**, não na desta nota. A nota tem de dizer o mesmo que o documento
    /// que corrige.
    /// </summary>
    public InvoicedParty Customer { get; private set; }

    public string Currency { get; private set; }

    /// <summary>
    /// Porquê. Obrigatório: uma nota de crédito sem motivo é dinheiro devolvido
    /// sem explicação, e é a primeira coisa que uma conferência pergunta.
    /// </summary>
    public string Reason { get; private set; }

    public string? FiscalNotice { get; private set; }

    public IReadOnlyList<CreditNoteLine> Lines => _lines;

    public decimal NetTotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static CreditNote Issue(
        DocumentNumber number,
        SalesInvoice invoice,
        DateOnly issuedOn,
        string reason,
        IReadOnlyList<NewInvoiceLine> lines,
        string? fiscalNotice = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(invoice);

        if (number.Type is not DocumentType.NC)
        {
            throw new ArgumentException(
                $"Uma nota de crédito numera-se em série NC, não {number.Type}.", nameof(number));
        }

        // Creditar uma factura anulada não faz sentido: já não há o que
        // corrigir. Deixar passar produziria duas correcções do mesmo facto.
        if (invoice.Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"A factura {invoice.Number.Formatted} está anulada e não se credita.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Uma nota de crédito regista sempre o motivo — é a primeira coisa que uma " +
                "conferência pergunta.",
                nameof(reason));
        }

        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("Uma nota de crédito tem pelo menos uma linha.", nameof(lines));
        }

        if (issuedOn < invoice.IssuedOn)
        {
            throw new ArgumentException(
                "Uma nota de crédito não é anterior à factura que corrige.", nameof(issuedOn));
        }

        var nota = new CreditNote(
            Guid.CreateVersion7(),
            number,
            invoice.Id,
            invoice.Number.Formatted,
            issuedOn,
            // O facto gerador é o da factura corrigida, não o de hoje: o imposto
            // que se devolve é o que foi liquidado (ADR-011 §3).
            invoice.TaxPointDate,
            invoice.CustomerId,
            invoice.Customer,
            invoice.Currency,
            reason.Trim(),
            string.IsNullOrWhiteSpace(fiscalNotice) ? null : fiscalNotice.Trim());

        var ordem = 1;

        foreach (var linha in lines)
        {
            nota._lines.Add(CreditNoteLine.Create(ordem++, linha));
        }

        nota.NetTotal = nota._lines.Sum(line => line.NetAmount);
        nota.TaxTotal = nota._lines.Sum(line => line.TaxAmount);
        nota.GrossTotal = nota.NetTotal + nota.TaxTotal;

        if (nota.GrossTotal <= 0)
        {
            throw new ArgumentException(
                "Uma nota de crédito de valor zero não corrige nada.", nameof(lines));
        }

        return nota;
    }

    /// <summary>
    /// Anula a nota de crédito. Não elimina (BR-14) — o crédito deixa de contar
    /// para o saldo da factura, e a linha continua na base.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"A nota {Number.Formatted} já está anulada desde {CancelledAt:yyyy-MM-dd}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Anular exige motivo.", nameof(reason));
        }

        Status = InvoiceStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = at;
    }
}

/// <summary>
/// Linha de nota de crédito. Imutável, como a da factura.
/// </summary>
public sealed class CreditNoteLine
{
    private CreditNoteLine(
        Guid id,
        int lineNumber,
        string description,
        decimal quantity,
        decimal unitPrice,
        string taxCode,
        decimal taxPercentage,
        decimal netAmount,
        decimal taxAmount)
    {
        Id = id;
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TaxCode = taxCode;
        TaxPercentage = taxPercentage;
        NetAmount = netAmount;
        TaxAmount = taxAmount;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private CreditNoteLine()
    {
        Description = string.Empty;
        TaxCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CreditNoteId { get; private set; }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public string TaxCode { get; private set; }

    public decimal TaxPercentage { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    internal static CreditNoteLine Create(int lineNumber, NewInvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line.Description))
        {
            throw new ArgumentException("Uma linha precisa de descrição.", nameof(line));
        }

        if (line.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.Quantity, "A quantidade é maior que zero.");
        }

        if (line.UnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.UnitPrice, "O preço unitário não é negativo.");
        }

        if (line.TaxPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.TaxPercentage, "Uma taxa está entre 0 e 100 por cento.");
        }

        // Mesmo arredondamento da factura, e pela mesma razão: o valor
        // exportado tem de ser o que o documento mostra.
        var liquido = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var imposto = Math.Round(liquido * line.TaxPercentage / 100m, 2, MidpointRounding.AwayFromZero);

        return new CreditNoteLine(
            Guid.CreateVersion7(),
            lineNumber,
            line.Description.Trim(),
            line.Quantity,
            line.UnitPrice,
            (line.TaxCode ?? string.Empty).Trim().ToUpperInvariant(),
            line.TaxPercentage,
            liquido,
            imposto);
    }
}
