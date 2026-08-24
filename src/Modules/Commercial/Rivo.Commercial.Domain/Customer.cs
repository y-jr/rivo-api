namespace Rivo.Commercial.Domain;

/// <summary>
/// Cliente. `commercial` é o dono confirmado (`modules/commercial.md`).
///
/// <para>
/// O conjunto de atributos não é escolha de desenho: é o que o SAF-T AO exige
/// do elemento <c>Customer</c>. O ADR-036 dispensou a certificação e manteve a
/// <em>forma</em> do documento, e a razão está no contrato de completude — o
/// que não for capturado no momento da transacção não se reconstrói depois.
/// Um cliente sem morada de facturação hoje é uma factura por corrigir amanhã.
/// </para>
///
/// <para>
/// <strong>A unicidade do NIF não vive aqui.</strong> É invariante sobre o
/// conjunto de clientes, não sobre um cliente, e o agregado não vê o conjunto.
/// Pertence a um índice único em `commercial.customer` mais a verificação na
/// camada Application — não se finge no domínio o que o domínio não pode
/// garantir.
/// </para>
/// </summary>
public sealed class Customer
{
    private Customer(Guid id, string name, string taxId, BillingAddress billingAddress)
    {
        Id = id;
        Name = name;
        TaxId = taxId;
        BillingAddress = billingAddress;
        Status = CustomerStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Customer()
    {
        Name = string.Empty;
        TaxId = string.Empty;
        BillingAddress = null!;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// NIF, normalizado sem espaços e em maiúsculas.
    ///
    /// <para>
    /// <strong>Sem validação de formato, e é deliberado.</strong> As regras de
    /// composição do NIF angolano não estão verificadas em fonte primária neste
    /// repositório, e `CLAUDE.md` proíbe implementar regras fiscais a partir de
    /// levantamento provisório. Um validador inventado recusaria clientes
    /// legítimos — falha pior do que a ausência de validação, porque parece
    /// correcta.
    /// </para>
    /// </summary>
    public string TaxId { get; private set; }

    public CustomerStatus Status { get; private set; }

    public BillingAddress BillingAddress { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025).
    ///
    /// <para>
    /// <strong>O domínio nunca lhe toca.</strong> Quem o incrementa é o
    /// `SaveChangesAsync` do DbContext, para todas as entidades alteradas de
    /// uma vez. Obrigar cada método que altera estado a lembrar-se disto seria
    /// uma regra que se esquece uma vez e falha em silêncio para sempre.
    /// </para>
    /// </summary>
    public int Version { get; private set; }

    public static Customer Register(string name, string taxId, BillingAddress billingAddress)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um cliente precisa de nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException(
                "Um cliente precisa de NIF — é o que o identifica no documento fiscal.", nameof(taxId));
        }

        ArgumentNullException.ThrowIfNull(billingAddress);

        return new Customer(Guid.CreateVersion7(), name.Trim(), Normalize(taxId), billingAddress);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um cliente precisa de nome.", nameof(name));
        }

        Name = name.Trim();
    }

    /// <summary>
    /// Corrige o NIF.
    ///
    /// <para>
    /// Não altera documentos já emitidos: eles guardam o NIF que vigorava, e é
    /// isso que faz a correcção ser segura. Alterar aqui e reescrever lá seria
    /// mudar o passado.
    /// </para>
    /// </summary>
    public void CorrectTaxId(string taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            throw new ArgumentException("Um cliente precisa de NIF.", nameof(taxId));
        }

        TaxId = Normalize(taxId);
    }

    public void ChangeBillingAddress(BillingAddress billingAddress)
    {
        ArgumentNullException.ThrowIfNull(billingAddress);

        BillingAddress = billingAddress;
    }

    /// <summary>Contactos são opcionais: nem todo o cliente os fornece.</summary>
    public void ChangeContacts(string? email, string? phone)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    /// <summary>
    /// Desactiva o cliente.
    ///
    /// <para>
    /// <strong>Nunca eliminar.</strong> BR-14 proíbe eliminação física de
    /// documentos fiscais, e um cliente referenciado por facturas emitidas é
    /// parte desses documentos. Desactivar é o que existe.
    /// </para>
    /// </summary>
    public void Deactivate()
    {
        if (Status is CustomerStatus.Inactive)
        {
            return;
        }

        Status = CustomerStatus.Inactive;
    }

    public void Reactivate()
    {
        if (Status is CustomerStatus.Active)
        {
            return;
        }

        Status = CustomerStatus.Active;
    }

    private static string Normalize(string taxId) =>
        taxId.Replace(" ", string.Empty).Trim().ToUpperInvariant();
}

public enum CustomerStatus
{
    Active,
    Inactive,
}

/// <summary>
/// Morada de facturação. Objecto de valor — substitui-se inteira, não se edita
/// campo a campo.
///
/// <para>
/// Os três campos são os que o SAF-T AO exige em <c>BillingAddress</c>. Uma
/// morada parcial passaria na aplicação e falharia na exportação, quando já não
/// houvesse como a completar.
/// </para>
/// </summary>
public sealed class BillingAddress
{
    public BillingAddress(string detail, string city, string country)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("A morada de facturação precisa de detalhe.", nameof(detail));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("A morada de facturação precisa de cidade.", nameof(city));
        }

        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
        {
            throw new ArgumentException(
                "O país é o código ISO 3166-1 alpha-2, com duas letras (`AO` para Angola).",
                nameof(country));
        }

        Detail = detail.Trim();
        City = city.Trim();
        Country = country.Trim().ToUpperInvariant();
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private BillingAddress()
    {
        Detail = string.Empty;
        City = string.Empty;
        Country = string.Empty;
    }

    public string Detail { get; private set; }

    public string City { get; private set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; private set; }
}
