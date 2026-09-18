namespace Rivo.Fiscal.Domain;

/// <summary>
/// A identidade fiscal da própria empresa — quem emite.
///
/// <para>
/// <strong>Faltava, e a falta só apareceu ao tentar imprimir uma factura.</strong>
/// O sistema sabia tudo sobre o cliente de cada documento (<c>InvoicedParty</c>,
/// congelado no momento da emissão) e nada sobre si mesmo: nem nome, nem número
/// de contribuinte, nem endereço. Como documento fiscal, uma factura sem
/// emitente identificado não é um documento — é um rascunho.
/// </para>
///
/// <para>
/// Pertence a <c>fiscal</c> e não a <c>finance</c> porque é o
/// <c>Header</c> do SAF-T AO, e o mapeamento secção→módulo de
/// <c>docs/rivo-fiscal-saft-ao-v1.md</c> §2 atribui o <c>Header</c> a este
/// módulo. Os nomes dos campos seguem os do XSD de propósito: quando a
/// exportação SAF-T for feita, o mapeamento é directo e não há tradução a
/// inventar.
/// </para>
///
/// <para>
/// <strong>É singular, e por isso não tem lista.</strong> O ADR-003 fixou
/// empresa única, sem multi-tenancy na v1 — há uma identidade fiscal, não um
/// catálogo delas. A chave é constante (<see cref="WellKnownId"/>) para que
/// "criar" e "corrigir" sejam a mesma operação do ponto de vista de quem chama,
/// e para que não exista maneira de haver duas.
/// </para>
///
/// <para>
/// <strong>Corrigir não reescreve o passado.</strong> Um documento já emitido
/// tem o seu ficheiro congelado, com o emitente que estava em vigor à data — a
/// mesma disciplina de <c>InvoicedParty</c> aplicada ao outro lado da relação.
/// Mudar a sede da empresa hoje não reimprime as facturas do ano passado.
/// </para>
/// </summary>
public sealed class TaxEntityProfile
{
    /// <summary>
    /// A chave, constante. Ver a nota sobre singularidade no resumo da classe.
    /// </summary>
    public static readonly Guid WellKnownId = new("0197c3f2-0000-7000-8000-000000000001");

    private TaxEntityProfile()
    {
        CompanyName = string.Empty;
        TaxRegistrationNumber = string.Empty;
        AddressDetail = string.Empty;
        City = string.Empty;
        Country = string.Empty;
    }

    private TaxEntityProfile(
        string companyName,
        string taxRegistrationNumber,
        string addressDetail,
        string city,
        string country)
    {
        Id = WellKnownId;
        CompanyName = companyName;
        TaxRegistrationNumber = taxRegistrationNumber;
        AddressDetail = addressDetail;
        City = city;
        Country = country;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Contador de concorrência optimista (ADR-002, ADR-025). Incrementado pela
    /// infraestrutura ao gravar, nunca pelo domínio.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Razão social. <c>Header/CompanyName</c> no SAF-T.</summary>
    public string CompanyName { get; private set; }

    /// <summary>
    /// Nome comercial, quando difere da razão social.
    /// <c>Header/BusinessName</c> — opcional no XSD e opcional aqui.
    /// </summary>
    public string? BusinessName { get; private set; }

    /// <summary>
    /// Número de Identificação Fiscal. <c>Header/TaxRegistrationNumber</c>.
    ///
    /// <para>
    /// Guardado como texto e não como número: um NIF é um identificador, não uma
    /// quantidade, e zeros à esquerda contam.
    /// </para>
    /// </summary>
    public string TaxRegistrationNumber { get; private set; }

    public string AddressDetail { get; private set; }

    public string City { get; private set; }

    public string? PostalCode { get; private set; }

    public string Country { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>
    /// Número de validação do software atribuído pela AGT.
    /// <c>Header/SoftwareValidationNumber</c>.
    ///
    /// <para>
    /// <strong>Existir este campo não é estar certificado.</strong> A
    /// certificação é o K7 e depende de terceiros; isto é só o sítio onde o
    /// número vive quando houver um. Enquanto estiver vazio, o documento
    /// impresso não o menciona — é melhor omitir do que imprimir um número
    /// falso.
    /// </para>
    /// </summary>
    public string? SoftwareValidationNumber { get; private set; }

    /// <summary>
    /// Cria a identidade fiscal. Só faz sentido uma vez — daí em diante é
    /// <see cref="Correct"/>.
    /// </summary>
    public static TaxEntityProfile Declare(
        string companyName,
        string taxRegistrationNumber,
        string addressDetail,
        string city,
        string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxRegistrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressDetail);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);

        return new TaxEntityProfile(
            companyName.Trim(),
            taxRegistrationNumber.Trim(),
            addressDetail.Trim(),
            city.Trim(),
            country.Trim());
    }

    /// <summary>
    /// Corrige os dados do emitente. Ver a nota sobre o passado no resumo da
    /// classe: isto não toca em nenhum documento já emitido.
    /// </summary>
    public void Correct(
        string companyName,
        string taxRegistrationNumber,
        string addressDetail,
        string city,
        string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxRegistrationNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressDetail);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);

        CompanyName = companyName.Trim();
        TaxRegistrationNumber = taxRegistrationNumber.Trim();
        AddressDetail = addressDetail.Trim();
        City = city.Trim();
        Country = country.Trim();
    }

    /// <summary>
    /// Preenche os campos opcionais. Separado de <see cref="Correct"/> porque
    /// nenhum deles é obrigatório e um valor vazio quer dizer "não tenho", não
    /// "erro".
    /// </summary>
    public void Describe(
        string? businessName,
        string? postalCode,
        string? email,
        string? phone,
        string? softwareValidationNumber)
    {
        BusinessName = Limpar(businessName);
        PostalCode = Limpar(postalCode);
        Email = Limpar(email);
        Phone = Limpar(phone);
        SoftwareValidationNumber = Limpar(softwareValidationNumber);
    }

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
