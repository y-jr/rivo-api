namespace Rivo.Finance.Domain;

/// <summary>
/// Factura de venda. `finance`/AR possui-a — `commercial` fornece a base do
/// cliente mas não emite nem possui a factura (`modules/commercial.md`).
///
/// <para>
/// <strong>Emite-se inteira, num acto só.</strong> Não há rascunho e não há
/// forma de acrescentar uma linha depois: o construtor recebe as linhas e a
/// partir daí só o cancelamento altera estado. A imutabilidade fica imposta
/// pela forma do agregado e não por uma verificação que alguém pode esquecer.
/// </para>
///
/// <para>
/// <strong>Não é documento fiscal válido em Angola</strong> — falta a
/// certificação da AGT e a cadeia <c>Hash</c>/<c>HashControl</c>, adiadas pelo
/// ADR-036. Tem a forma, não tem a conformidade.
/// </para>
/// </summary>
public sealed class SalesInvoice
{
    private readonly List<SalesInvoiceLine> _lines = [];

    private SalesInvoice(
        Guid id,
        DocumentNumber number,
        DateOnly issuedOn,
        DateOnly taxPointDate,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        string? fiscalNotice)
    {
        Id = id;
        Number = number;
        IssuedOn = issuedOn;
        TaxPointDate = taxPointDate;
        CustomerId = customerId;
        Customer = customer;
        Currency = currency;
        FiscalNotice = fiscalNotice;
        Status = InvoiceStatus.Normal;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private SalesInvoice()
    {
        Number = null!;
        Customer = null!;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public DocumentNumber Number { get; private set; }

    /// <summary>Data do documento.</summary>
    public DateOnly IssuedOn { get; private set; }

    /// <summary>
    /// Data do facto gerador, que determina que taxa se aplica (ADR-011 §3).
    ///
    /// <para>
    /// Distinta de <see cref="IssuedOn"/> ainda que coincidam no caso corrente:
    /// uma factura emitida em Janeiro sobre um serviço prestado em Dezembro
    /// liquida o imposto de Dezembro.
    /// </para>
    /// </summary>
    public DateOnly TaxPointDate { get; private set; }

    /// <summary>
    /// Estado do SAF-T: <c>N</c> normal, <c>A</c> anulado. Não há mais nenhum,
    /// e em particular não há eliminado (BR-14).
    /// </summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>
    /// O cliente registado em `commercial`, ou <c>null</c> numa venda a
    /// consumidor final.
    ///
    /// <para>
    /// Nulo não é ausência de dados: <see cref="Customer"/> continua preenchido
    /// com o que a factura tem de mostrar. O que falta é a ligação a um
    /// registo, porque não há registo — a pessoa não se identificou.
    /// </para>
    /// </summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>
    /// O cliente <strong>tal como estava no momento da emissão</strong>.
    ///
    /// <para>
    /// <strong>Isto não contraria BR-18.</strong> A regra proíbe cópias
    /// operacionais que ficam obsoletas em silêncio — a morada actual de alguém,
    /// o departamento de agora. Uma factura é facto histórico, e o seu conteúdo
    /// tem de reflectir o que era verdade quando foi emitida. Resolver o cliente
    /// ao vivo faria uma correcção de nome reescrever retroactivamente todas as
    /// facturas passadas, que é exactamente o que a imutabilidade proíbe.
    /// </para>
    ///
    /// <para>
    /// É o mesmo princípio de BR-6 em `approval`: contexto congelado na
    /// submissão, política como rasto e não como chave estrangeira viva.
    /// <see cref="CustomerId"/> continua lá para quem quiser o cliente de hoje.
    /// </para>
    /// </summary>
    public InvoicedParty Customer { get; private set; }

    /// <summary>ISO 4217. <c>AOA</c> no caso corrente.</summary>
    public string Currency { get; private set; }

    public IReadOnlyList<SalesInvoiceLine> Lines => _lines;

    public decimal NetTotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal GrossTotal { get; private set; }

    /// <summary>
    /// A menção que declara que este documento não é fiscalmente válido, ou
    /// <c>null</c> quando o sistema estiver certificado.
    ///
    /// <para>
    /// <strong>Congelada na emissão, como tudo o resto.</strong> É o ponto: no
    /// dia em que houver <c>SoftwareValidationNumber</c>, as facturas emitidas
    /// <em>antes</em> continuam a não ser válidas, e a menção tem de continuar
    /// a aparecer nelas. Derivá-la em tempo de leitura apagaria a marca de todo
    /// o histórico no momento da certificação — exactamente o contrário do que
    /// se quer.
    /// </para>
    /// </summary>
    public string? FiscalNotice { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025). O domínio nunca lhe toca — quem o
    /// incrementa é o <c>SaveChangesAsync</c> do DbContext.
    /// </summary>
    public int Version { get; private set; }

    /// <param name="customerId">
    /// Nulo numa venda a consumidor final. Em qualquer outro caso é
    /// obrigatório: uma factura a um cliente registado que perdesse a ligação
    /// deixaria de ser rastreável até ele.
    /// </param>
    /// <param name="fiscalNotice">
    /// Menção de não-validade fiscal, congelada na emissão. Nula só quando o
    /// sistema estiver certificado.
    /// </param>
    public static SalesInvoice Issue(
        DocumentNumber number,
        DateOnly issuedOn,
        DateOnly taxPointDate,
        Guid? customerId,
        InvoicedParty customer,
        string currency,
        IReadOnlyList<NewInvoiceLine> lines,
        string? fiscalNotice = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(customer);

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um identificador vazio não é o mesmo que ausência de cliente. " +
                "Para consumidor final, passe nulo.",
                nameof(customerId));
        }

        // As duas metades têm de bater certo. Um consumidor final com
        // identificador de cliente, ou um cliente registado sem ele, é um
        // engano de quem chama — e passaria despercebido na exportação.
        if (customer.IsFinalConsumer != (customerId is null))
        {
            throw new ArgumentException(
                customer.IsFinalConsumer
                    ? "Uma factura a consumidor final não tem cliente registado."
                    : "Uma factura a um cliente registado precisa do identificador dele.",
                nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "A moeda é o código ISO 4217, com três letras (`AOA`).", nameof(currency));
        }

        // Uma factura sem linhas não factura nada. Passaria na aplicação e
        // apareceria na exportação com total zero e sem explicação.
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("Uma factura tem pelo menos uma linha.", nameof(lines));
        }

        if (taxPointDate > issuedOn)
        {
            throw new ArgumentException(
                "O facto gerador não pode ser posterior à data do documento.", nameof(taxPointDate));
        }

        var factura = new SalesInvoice(
            Guid.CreateVersion7(), number, issuedOn, taxPointDate, customerId, customer,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(fiscalNotice) ? null : fiscalNotice.Trim());

        var ordem = 1;

        foreach (var linha in lines)
        {
            factura._lines.Add(SalesInvoiceLine.Create(ordem++, linha));
        }

        // Os totais são a soma dos valores já arredondados de cada linha, e não
        // o arredondamento da soma. É o que o SAF-T exporta linha a linha —
        // somar por cima dos valores exactos daria um total que não bate com a
        // soma visível no documento.
        factura.NetTotal = factura._lines.Sum(line => line.NetAmount);
        factura.TaxTotal = factura._lines.Sum(line => line.TaxAmount);
        factura.GrossTotal = factura.NetTotal + factura.TaxTotal;

        return factura;
    }

    /// <summary>
    /// Anula a factura.
    ///
    /// <para>
    /// <strong>É a única alteração possível depois da emissão</strong>, e não
    /// apaga nada: o estado passa a <c>A</c> e a linha continua na base (BR-14).
    /// Não existe método de eliminação, por desenho.
    /// </para>
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"A factura {Number.Formatted} já está anulada desde {CancelledAt:yyyy-MM-dd}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Anular uma factura exige motivo — é o que fica para quem conferir depois.",
                nameof(reason));
        }

        Status = InvoiceStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = at;
    }
}

/// <summary>Estados do SAF-T. Não há eliminado (BR-14).</summary>
public enum InvoiceStatus
{
    /// <summary><c>N</c> — normal.</summary>
    Normal,

    /// <summary><c>A</c> — anulado.</summary>
    Cancelled,
}

/// <summary>
/// O cliente congelado no momento da emissão. Objecto de valor — não tem
/// identidade própria, vive nas colunas da factura.
/// </summary>
public sealed class InvoicedParty
{
    /// <summary>
    /// Venda a quem não se identificou.
    ///
    /// <para>
    /// <strong>O NIF vem de configuração e não daqui.</strong> A convenção
    /// angolana para o identificador de consumidor final não está verificada em
    /// fonte primária neste repositório, e `CLAUDE.md` proíbe implementar
    /// regras fiscais a partir de levantamento provisório. Fixar aqui um valor
    /// dar-lhe-ia ar de código oficial verificado, que é a falha pior — parece
    /// correcta.
    /// </para>
    ///
    /// <para>
    /// Enquanto o ADR-036 valer, o documento não é fiscalmente válido de
    /// qualquer forma, e o valor configurado serve de marcador. <strong>Tem de
    /// ser substituído pelo oficial antes de qualquer certificação.</strong>
    /// </para>
    /// </summary>
    public static InvoicedParty FinalConsumer(string taxId, string name)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException(
                "Facturar a consumidor final exige o identificador convencionado, " +
                "que vem de configuração (Finance:FinalConsumerTaxId).",
                nameof(taxId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("O consumidor final precisa de designação.", nameof(name));
        }

        return new InvoicedParty(name.Trim(), taxId.Trim(), finalConsumer: true);
    }

    private InvoicedParty(string name, string taxId, bool finalConsumer)
    {
        Name = name;
        TaxId = taxId;
        IsFinalConsumer = finalConsumer;

        // Sem morada, e não é omissão: quem não se identifica também não dá
        // morada. Os campos ficam vazios em vez de inventados.
        AddressDetail = string.Empty;
        City = string.Empty;
        Country = string.Empty;
    }

    public InvoicedParty(string name, string taxId, string addressDetail, string city, string country)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A factura regista o nome do cliente.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException("A factura regista o NIF do cliente.", nameof(taxId));
        }

        if (string.IsNullOrWhiteSpace(addressDetail) || string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("A factura regista a morada de facturação.", nameof(addressDetail));
        }

        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
        {
            throw new ArgumentException("O país é o código ISO 3166-1 alpha-2.", nameof(country));
        }

        Name = name.Trim();
        TaxId = taxId.Trim();
        AddressDetail = addressDetail.Trim();
        City = city.Trim();
        Country = country.Trim().ToUpperInvariant();
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private InvoicedParty()
    {
        Name = string.Empty;
        TaxId = string.Empty;
        AddressDetail = string.Empty;
        City = string.Empty;
        Country = string.Empty;
    }

    public string Name { get; private set; }

    public string TaxId { get; private set; }

    public string AddressDetail { get; private set; }

    public string City { get; private set; }

    public string Country { get; private set; }

    /// <summary>
    /// Verdadeiro numa venda a quem não se identificou. Distingue-se de um
    /// cliente registado com morada em falta — aqui a morada está vazia porque
    /// não existe, não porque falta preencher.
    /// </summary>
    public bool IsFinalConsumer { get; private set; }
}

/// <param name="TaxPercentage">
/// Vem de `fiscal`, determinada à data do facto gerador. A factura guarda-a —
/// congelada, como o cliente — para que a exportação reproduza o documento tal
/// como foi emitido, mesmo que a taxa mude depois.
/// </param>
public sealed record NewInvoiceLine(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode,
    decimal TaxPercentage);

/// <summary>
/// Linha de factura. Imutável: criada com a factura e nunca alterada.
/// </summary>
public sealed class SalesInvoiceLine
{
    private SalesInvoiceLine(
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
    private SalesInvoiceLine()
    {
        Description = string.Empty;
        TaxCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid SalesInvoiceId { get; private set; }

    /// <summary>Ordem no documento, a começar em 1.</summary>
    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public string TaxCode { get; private set; }

    public decimal TaxPercentage { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal TaxAmount { get; private set; }

    internal static SalesInvoiceLine Create(int lineNumber, NewInvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line.Description))
        {
            throw new ArgumentException("Uma linha de factura precisa de descrição.", nameof(line));
        }

        if (line.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.Quantity, "A quantidade de uma linha é maior que zero.");
        }

        if (line.UnitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.UnitPrice, "O preço unitário não é negativo.");
        }

        if (string.IsNullOrWhiteSpace(line.TaxCode))
        {
            throw new ArgumentException(
                "Uma linha de factura precisa do código de imposto.", nameof(line));
        }

        if (line.TaxPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.TaxPercentage, "Uma taxa está entre 0 e 100 por cento.");
        }

        // Arredondamento a duas casas, meio para cima. Explícito porque o valor
        // exportado tem de ser o mesmo que o documento mostra: deixar o
        // arredondamento para a apresentação faria a soma das linhas visíveis
        // não bater com o total gravado.
        var liquido = Round(line.Quantity * line.UnitPrice);
        var imposto = Round(liquido * line.TaxPercentage / 100m);

        return new SalesInvoiceLine(
            Guid.CreateVersion7(),
            lineNumber,
            line.Description.Trim(),
            line.Quantity,
            line.UnitPrice,
            line.TaxCode.Trim().ToUpperInvariant(),
            line.TaxPercentage,
            liquido,
            imposto);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
