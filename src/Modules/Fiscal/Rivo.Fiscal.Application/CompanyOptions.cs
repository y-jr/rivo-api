namespace Rivo.Fiscal.Application;

/// <summary>
/// A identidade da própria empresa, para o `Header` do SAF-T (ADR-058).
///
/// <para>
/// <strong>Configuração e não entidade</strong>, porque o Rivo é de uma
/// empresa só (ADR-003): a empresa <em>é</em> o deployment. Uma tabela com uma
/// linha garantida seria uma entidade que nunca tem mais do que um elemento e
/// mesmo assim exige listagem, criação, e a pergunta "e se houver duas?" em
/// cada consulta.
/// </para>
///
/// <para>
/// ⚠ Se o ADR-003 for revertido, isto tem de ser revertido com ele.
/// </para>
/// </summary>
public sealed class CompanyOptions
{
    public const string SectionName = "Company";

    /// <summary>Razão social. `CompanyName` no SAF-T, máx. 200 caracteres.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// NIF da empresa. `TaxRegistrationNumber` e também `CompanyID`.
    /// </summary>
    public string? TaxRegistrationNumber { get; init; }

    /// <summary>Nome comercial, se diferente da razão social. Opcional no XSD.</summary>
    public string? BusinessName { get; init; }

    public CompanyAddressOptions Address { get; init; } = new();

    /// <summary>
    /// `TaxEntity` — a que estabelecimento respeita o ficheiro. `"Global"`
    /// quando é a empresa toda, que é o caso de uma instalação sem
    /// multi-tenancy.
    /// </summary>
    public string TaxEntity { get; init; } = "Global";

    /// <summary>
    /// `TaxAccountingBasis` — o tipo de programa que produz o ficheiro. O XSD
    /// enumera oito valores; o Rivo é `"I"`, contabilidade integrada com a
    /// facturação, porque emite documentos **e** lança nos livros na mesma
    /// transacção.
    /// </summary>
    public string TaxAccountingBasis { get; init; } = "I";

    /// <summary>
    /// `ProductCompanyTaxID` — o NIF de quem produz o software, não de quem o
    /// usa. Distinto de <see cref="TaxRegistrationNumber"/>, e é fácil
    /// confundi-los.
    /// </summary>
    public string ProductCompanyTaxID { get; init; } = "999999999";

    /// <summary>
    /// `SoftwareValidationNumber` — o número de certificação da AGT.
    ///
    /// <para>
    /// <strong>`"0"` por omissão, e é verdade e não omissão</strong>: o Rivo
    /// não está certificado (ADR-036), e o XSD admite `"0"` precisamente para
    /// esse caso. Configurável para o dia em que houver número.
    /// </para>
    /// </summary>
    public string SoftwareValidationNumber { get; init; } = "0";

    /// <summary>Moeda do ficheiro. `AOA` salvo indicação em contrário.</summary>
    public string CurrencyCode { get; init; } = "AOA";

    public string? Telephone { get; init; }

    public string? Email { get; init; }

    public string? Website { get; init; }

    /// <summary>O que o XSD exige de um <c>SAFAOAngolaVatNumber</c>.</summary>
    private const int NifMinimo = 10;

    private const int NifMaximo = 15;

    /// <summary>
    /// O que impede esta configuração de produzir um SAF-T válido.
    ///
    /// <para>
    /// Verificado no arranque e não na exportação: um `.env` mal preenchido
    /// rebenta ao levantar a aplicação, dizendo qual o campo. Uma verificação
    /// só na exportação rebentaria meses depois, no dia em que alguém
    /// precisasse do ficheiro.
    /// </para>
    ///
    /// <para>
    /// ⚠ <strong>Não basta não estar vazio.</strong> Até 2026-09-08 esta
    /// verificação só testava ausência, e um NIF de nove dígitos levantava a
    /// aplicação sem uma queixa para depois produzir um ficheiro que a AGT
    /// recusa — o comprimento é do XSD (<c>SAFAOAngolaVatNumber</c>: 10 a 15),
    /// e uma verificação de arranque que não o conheça não serve para o que
    /// existe.
    /// </para>
    ///
    /// <para>
    /// As mensagens são frases e não nomes de campo: quem as lê está a olhar
    /// para um `.env` e precisa de saber o que pôr, não só onde.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> CamposEmFalta()
    {
        List<string> problemas = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            problemas.Add(
                $"{SectionName}:{nameof(Name)} está por preencher — é a razão social "
                + "que sai no SAF-T.");
        }

        if (string.IsNullOrWhiteSpace(TaxRegistrationNumber))
        {
            problemas.Add(
                $"{SectionName}:{nameof(TaxRegistrationNumber)} está por preencher — é o "
                + "NIF da empresa.");
        }
        else if (TaxRegistrationNumber.Trim().Length is < NifMinimo or > NifMaximo)
        {
            problemas.Add(
                $"{SectionName}:{nameof(TaxRegistrationNumber)} tem "
                + $"{TaxRegistrationNumber.Trim().Length} caracteres; o SAF-T exige entre "
                + $"{NifMinimo} e {NifMaximo}.");
        }

        return problemas;
    }
}

/// <summary>
/// A morada da sede. `AddressStructureAO` no XSD, onde só `AddressDetail`,
/// `City` e `Country` são obrigatórios — e `Country` é fixo em `"AO"`.
/// </summary>
public sealed class CompanyAddressOptions
{
    public string? BuildingNumber { get; init; }

    public string? StreetName { get; init; }

    /// <summary>
    /// `AddressDetail` — obrigatório no XSD. Sem ele configurado, compõe-se de
    /// <see cref="StreetName"/> e <see cref="BuildingNumber"/>; sem nada,
    /// `"Desconhecido"`, porque um ficheiro sem morada não valida e recusar a
    /// exportação inteira por causa disto seria pior.
    /// </summary>
    public string? AddressDetail { get; init; }

    public string City { get; init; } = "Luanda";

    public string? PostalCode { get; init; }

    public string? Province { get; init; }

    /// <summary>
    /// O XSD fixa `Country` em `"AO"` — não é configurável, e não está aqui
    /// para não dar a ideia de que é.
    /// </summary>
    public string DetalheEfectivo =>
        !string.IsNullOrWhiteSpace(AddressDetail)
            ? AddressDetail
            : string.Join(" ", new[] { StreetName, BuildingNumber }
                .Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } composto
                ? composto
                : "Desconhecido";
}
