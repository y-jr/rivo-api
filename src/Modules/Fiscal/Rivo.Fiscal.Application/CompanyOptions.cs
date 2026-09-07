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

    /// <summary>
    /// As duas que não têm valor por omissão possível.
    ///
    /// <para>
    /// Verificadas no arranque e não na exportação: um `.env` incompleto
    /// rebenta ao levantar a aplicação, com o nome do campo em falta. Uma
    /// verificação na exportação rebentaria meses depois, no dia em que
    /// alguém precisasse do ficheiro.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> CamposEmFalta()
    {
        List<string> faltam = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            faltam.Add($"{SectionName}:{nameof(Name)}");
        }

        if (string.IsNullOrWhiteSpace(TaxRegistrationNumber))
        {
            faltam.Add($"{SectionName}:{nameof(TaxRegistrationNumber)}");
        }

        return faltam;
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
