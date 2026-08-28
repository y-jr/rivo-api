namespace Rivo.Finance.Domain;

/// <summary>
/// Factura de compra. O que se deve a um fornecedor.
///
/// <para>
/// <strong>O número é do fornecedor, não nosso.</strong> Ao contrário da
/// factura de venda, esta não se numera em série do Rivo — chega já numerada, e
/// o número é dele. Confundir os dois faria o sistema numerar documentos que não
/// emitiu, que é a forma mais directa de partir a sequência auditável.
/// </para>
///
/// <para>
/// O fornecedor pertence a `procurement`. Guarda-se sempre o retrato — nome e
/// NIF — como se faz com o cliente na factura de venda, porque o documento é
/// facto histórico e o retrato é o que vigorava à data (BR-18). O identificador
/// acompanha quando o fornecedor está qualificado em `procurement`
/// (`ISupplierDirectory`); fica nulo para despesas que nunca passam por lá —
/// uma factura de electricidade não tem Fornecedor para qualificar.
/// </para>
/// </summary>
public sealed class PurchaseInvoice
{
    private PurchaseInvoice(
        Guid id,
        string supplierInvoiceNumber,
        Guid? supplierId,
        string supplierName,
        string supplierTaxId,
        DateOnly issuedOn,
        DateOnly dueOn,
        string currency,
        decimal netTotal,
        decimal taxTotal,
        string? description)
    {
        Id = id;
        SupplierInvoiceNumber = supplierInvoiceNumber;
        SupplierId = supplierId;
        SupplierName = supplierName;
        SupplierTaxId = supplierTaxId;
        IssuedOn = issuedOn;
        DueOn = dueOn;
        Currency = currency;
        NetTotal = netTotal;
        TaxTotal = taxTotal;
        GrossTotal = netTotal + taxTotal;
        Description = description;
        Status = InvoiceStatus.Normal;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PurchaseInvoice()
    {
        SupplierInvoiceNumber = string.Empty;
        SupplierName = string.Empty;
        SupplierTaxId = string.Empty;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>O número que o fornecedor pôs no documento dele.</summary>
    public string SupplierInvoiceNumber { get; private set; }

    /// <summary>Nulo quando o fornecedor não está qualificado em `procurement`.</summary>
    public Guid? SupplierId { get; private set; }

    public string SupplierName { get; private set; }

    /// <summary>NIF do fornecedor, normalizado sem espaços.</summary>
    public string SupplierTaxId { get; private set; }

    /// <summary>O fornecedor como retrato, para quem precisa dele junto.</summary>
    public PayeeParty Supplier => new(SupplierName, SupplierTaxId);

    public DateOnly IssuedOn { get; private set; }

    /// <summary>Data de vencimento. É o que ordena a fila de pagamentos.</summary>
    public DateOnly DueOn { get; private set; }

    public string Currency { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    public string? Description { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static PurchaseInvoice Register(
        string supplierInvoiceNumber,
        Guid? supplierId,
        PayeeParty supplier,
        DateOnly issuedOn,
        DateOnly dueOn,
        string currency,
        decimal netTotal,
        decimal taxTotal,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        if (string.IsNullOrWhiteSpace(supplierInvoiceNumber))
        {
            throw new ArgumentException(
                "Uma factura de compra traz o número do fornecedor — é por ele que se " +
                "reconcilia com o documento dele.",
                nameof(supplierInvoiceNumber));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("A moeda é o código ISO 4217.", nameof(currency));
        }

        if (netTotal <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(netTotal), netTotal, "Uma factura de compra de valor zero não deve nada.");
        }

        if (taxTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(taxTotal), taxTotal, "O imposto não é negativo.");
        }

        if (dueOn < issuedOn)
        {
            throw new ArgumentException(
                "O vencimento não é anterior à emissão.", nameof(dueOn));
        }

        return new PurchaseInvoice(
            Guid.CreateVersion7(),
            supplierInvoiceNumber.Trim().ToUpperInvariant(),
            supplierId,
            // Achatado em duas colunas em vez de tipo owned, e por uma razão
            // concreta: a garantia contra registar a mesma factura do mesmo
            // fornecedor duas vezes é um índice único sobre (NIF, número), e o
            // EF Core não sabe exprimir um índice que atravesse uma coluna de
            // tipo owned e uma do pai. `PayeeParty` continua a validar.
            supplier.Name,
            supplier.TaxId,
            issuedOn,
            dueOn,
            currency.Trim().ToUpperInvariant(),
            Math.Round(netTotal, 2, MidpointRounding.AwayFromZero),
            Math.Round(taxTotal, 2, MidpointRounding.AwayFromZero),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    /// <summary>
    /// Anula a factura de compra. Não elimina (BR-14) — deixa de dever, e a
    /// linha continua.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"A factura {SupplierInvoiceNumber} já está anulada desde {CancelledAt:yyyy-MM-dd}.");
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
/// A quem se paga, congelado. Objecto de valor, como o cliente na factura de
/// venda e pela mesma razão: um pagamento é facto histórico e tem de dizer a
/// quem foi na altura.
/// </summary>
public sealed class PayeeParty
{
    public PayeeParty(string name, string taxId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O fornecedor precisa de nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException(
                "O fornecedor precisa de NIF — é o que o identifica no documento dele.",
                nameof(taxId));
        }

        Name = name.Trim();
        TaxId = taxId.Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PayeeParty()
    {
        Name = string.Empty;
        TaxId = string.Empty;
    }

    public string Name { get; private set; }

    public string TaxId { get; private set; }
}
