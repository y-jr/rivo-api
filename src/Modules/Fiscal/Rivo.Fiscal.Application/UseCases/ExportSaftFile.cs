using System.Xml.Linq;
using Rivo.Fiscal.Application.Abstractions;

namespace Rivo.Fiscal.Application.UseCases;

/// <summary>
/// Gera o ficheiro SAF-T AO de um período.
///
/// <para>
/// <strong>O ficheiro cresce por secções, e cada estado intermédio é
/// válido.</strong> No XSD só o <c>Header</c> é obrigatório
/// (<c>minOccurs="1"</c>); <c>MasterFiles</c>, <c>GeneralLedgerEntries</c> e
/// <c>SourceDocuments</c> são todos opcionais. Isso não é uma folga a
/// explorar — é o que permite entregar isto por partes sem nunca produzir um
/// ficheiro inválido.
/// </para>
///
/// <para>
/// <strong>Estado: <c>Header</c> e a tabela de clientes.</strong> Faltam
/// fornecedores (`procurement`), produtos (`inventory`), plano de contas e
/// tabela de taxas. Cada um entra pela mesma porta
/// (<see cref="ISaftMasterData"/>) e é validado contra o XSD ao entrar.
/// </para>
///
/// <para>
/// ⚠ <strong>O ficheiro não tem validade legal.</strong>
/// <c>SoftwareValidationNumber</c> vai a <c>"0"</c>, que o XSD admite para
/// software não certificado — e é verdade, não omissão: o Rivo não está
/// certificado pela AGT (ADR-036). Um ficheiro válido na forma não é um
/// ficheiro aceite.
/// </para>
///
/// <para>
/// <strong>Sem cadeia de assinatura</strong> (K7). <c>Hash</c> e
/// <c>HashControl</c> só existem nos documentos de <c>SourceDocuments</c>, que
/// esta versão não emite — quando emitir, a cadeia tem de existir primeiro.
/// </para>
/// </summary>
public sealed class ExportSaftFile(
    CompanyOptions company,
    ISaftMasterData masterData,
    TimeProvider clock)
{
    /// <summary>O espaço de nomes do SAF-T AO 1.01_01, tal como o XSD o fixa.</summary>
    public static readonly XNamespace Ns = "urn:OECD:StandardAuditFile-Tax:AO_1.01_01";

    public const string AuditFileVersion = "1.01_01";

    /// <summary>
    /// O que vai em <c>AccountID</c> enquanto o plano de contas não existir.
    ///
    /// <para>
    /// <strong>É o valor que o XSD prevê para este caso</strong>, e não uma
    /// improvisação: a documentação do elemento diz «deve ser indicada a
    /// respectiva conta-corrente do cliente no plano de contas da
    /// contabilidade, <em>caso esteja definida. Caso contrário deve ser
    /// preenchido com a designação "Desconhecido"</em>», e o padrão do tipo
    /// admite-o explicitamente. O ADR-037 recusou inventar o PGC angolano —
    /// dizer "desconhecido" é o que resta, e é verdade.
    /// </para>
    /// </summary>
    private const string ContaDesconhecida = "Desconhecido";

    /// <summary>
    /// <c>0</c> — sem autofacturação. O Rivo não a faz: não há caso de uso em
    /// que o cliente emita a factura em nome da empresa. Quando houver, deixa
    /// de ser constante e passa a ser facto de `commercial`.
    /// </summary>
    private const string SemAutofacturacao = "0";

    public async Task<ExportSaftResult> ExecuteAsync(
        int fiscalYear,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return ExportSaftResult.Rejected("A data inicial é posterior à data final.");
        }

        if (from.Year != fiscalYear || to.Year != fiscalYear)
        {
            // O XSD não o impõe, mas um ficheiro cujo período atravessa o ano
            // declarado é incoerente com o `FiscalYear` que ele próprio anuncia.
            return ExportSaftResult.Rejected(
                $"O período tem de cair inteiro no exercício de {fiscalYear}.");
        }

        var faltam = company.CamposEmFalta();

        if (faltam.Count > 0)
        {
            // Não deveria chegar aqui — a aplicação recusa arrancar sem estes
            // campos (ADR-058). Fica como rede: se a verificação de arranque
            // for alguma vez relaxada, isto recusa em vez de emitir um
            // ficheiro com o nome da empresa em branco.
            return ExportSaftResult.Rejected(
                $"Identidade da empresa incompleta: {string.Join(", ", faltam)}.");
        }

        var clientes = await masterData.ListCustomersAsync(cancellationToken);

        var repetido = clientes
            .GroupBy(c => c.CustomerId, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (repetido is not null)
        {
            // `CustomerIDConstraint` no XSD. O validador apanharia isto, mas
            // com uma mensagem sobre chaves duplicadas que não diz a quem
            // exporta o que fazer. Recusar aqui nomeia o cliente.
            return ExportSaftResult.Rejected(
                $"Há mais do que um cliente com o identificador '{repetido.Key}'. "
                + "O SAF-T exige que seja único no ficheiro.");
        }

        var documento = new XDocument(
            new XDeclaration("1.0", "windows-1252", null),
            new XElement(
                Ns + "AuditFile",
                Header(fiscalYear, from, to),

                // Presente mesmo quando não tem filhos: o XSD declara
                // `MasterFiles` com `minOccurs` implícito de 1 e todos os
                // filhos opcionais. Um elemento vazio é o que diz "não há
                // dados de referência", que é diferente de não dizer nada.
                new XElement(Ns + "MasterFiles", clientes.Select(Cliente))));

        return ExportSaftResult.Generated(documento);
    }

    /// <summary>
    /// Um elemento <c>Customer</c>.
    ///
    /// <para>
    /// A ordem dos filhos não é estética: o XSD usa <c>xs:sequence</c>, e um
    /// ficheiro com os mesmos campos por outra ordem é inválido. É por isso
    /// que os testes validam contra o esquema em vez de conferirem campos.
    /// </para>
    /// </summary>
    private XElement Cliente(SaftCustomer cliente)
    {
        var elementos = new List<XObject>
        {
            new XElement(Ns + "CustomerID", cliente.CustomerId),
            new XElement(Ns + "AccountID", ContaDesconhecida),
            new XElement(Ns + "CustomerTaxID", cliente.TaxId),
            new XElement(Ns + "CompanyName", cliente.Name),
            Morada(Ns + "BillingAddress", cliente.BillingAddress),
            new XElement(Ns + "SelfBillingIndicator", SemAutofacturacao),
        };

        return new XElement(Ns + "Customer", elementos);
    }

    /// <summary>
    /// Uma morada de terceiro, na forma <c>AddressStructure</c> do XSD.
    ///
    /// <para>
    /// Distinta de <see cref="Morada()"/>, que é a da própria empresa: aquela
    /// lê da configuração e fixa o país em <c>AO</c>; esta recebe o país,
    /// porque um cliente pode não ser angolano.
    /// </para>
    /// </summary>
    private XElement Morada(XName nome, SaftAddress morada) =>
        new(
            nome,
            new XElement(Ns + "AddressDetail", morada.Detail),
            new XElement(Ns + "City", morada.City),
            new XElement(Ns + "Country", morada.Country));

    private XElement Header(int fiscalYear, DateOnly from, DateOnly to)
    {
        var elementos = new List<XObject>
        {
            new XElement(Ns + "AuditFileVersion", AuditFileVersion),

            // `CompanyID` e `TaxRegistrationNumber` são o mesmo número numa
            // empresa angolana sem registo comercial separado. O XSD trata-os
            // como campos distintos porque noutras jurisdições não coincidem.
            new XElement(Ns + "CompanyID", company.TaxRegistrationNumber!),
            new XElement(Ns + "TaxRegistrationNumber", company.TaxRegistrationNumber!),
            new XElement(Ns + "TaxAccountingBasis", company.TaxAccountingBasis),
            new XElement(Ns + "CompanyName", company.Name!),
        };

        if (!string.IsNullOrWhiteSpace(company.BusinessName))
        {
            elementos.Add(new XElement(Ns + "BusinessName", company.BusinessName));
        }

        elementos.Add(Morada());
        elementos.Add(new XElement(Ns + "FiscalYear", fiscalYear));
        elementos.Add(new XElement(Ns + "StartDate", from.ToString("yyyy-MM-dd")));
        elementos.Add(new XElement(Ns + "EndDate", to.ToString("yyyy-MM-dd")));
        elementos.Add(new XElement(Ns + "CurrencyCode", company.CurrencyCode));

        // A data de criação é a de agora, e não a do período: é quando o
        // ficheiro foi produzido, e é isso que a AGT usa para saber que
        // extracção está a ver.
        elementos.Add(new XElement(
            Ns + "DateCreated",
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd")));

        elementos.Add(new XElement(Ns + "TaxEntity", company.TaxEntity));
        elementos.Add(new XElement(Ns + "ProductCompanyTaxID", company.ProductCompanyTaxID));
        elementos.Add(new XElement(
            Ns + "SoftwareValidationNumber", company.SoftwareValidationNumber));
        elementos.Add(new XElement(Ns + "ProductID", "Rivo/Rivo Suite"));
        elementos.Add(new XElement(Ns + "ProductVersion", "1.0"));

        if (!string.IsNullOrWhiteSpace(company.Telephone))
        {
            elementos.Add(new XElement(Ns + "Telephone", company.Telephone));
        }

        if (!string.IsNullOrWhiteSpace(company.Email))
        {
            elementos.Add(new XElement(Ns + "Email", company.Email));
        }

        if (!string.IsNullOrWhiteSpace(company.Website))
        {
            elementos.Add(new XElement(Ns + "Website", company.Website));
        }

        return new XElement(Ns + "Header", elementos);
    }

    private XElement Morada()
    {
        var partes = new List<XObject>();

        if (!string.IsNullOrWhiteSpace(company.Address.BuildingNumber))
        {
            partes.Add(new XElement(Ns + "BuildingNumber", company.Address.BuildingNumber));
        }

        if (!string.IsNullOrWhiteSpace(company.Address.StreetName))
        {
            partes.Add(new XElement(Ns + "StreetName", company.Address.StreetName));
        }

        partes.Add(new XElement(Ns + "AddressDetail", company.Address.DetalheEfectivo));
        partes.Add(new XElement(Ns + "City", company.Address.City));

        if (!string.IsNullOrWhiteSpace(company.Address.PostalCode))
        {
            partes.Add(new XElement(Ns + "PostalCode", company.Address.PostalCode));
        }

        if (!string.IsNullOrWhiteSpace(company.Address.Province))
        {
            partes.Add(new XElement(Ns + "Province", company.Address.Province));
        }

        // Fixo em "AO" pelo XSD — não vem da configuração de propósito.
        partes.Add(new XElement(Ns + "Country", "AO"));

        return new XElement(Ns + "CompanyAddress", partes);
    }
}

public sealed record ExportSaftResult(ExportSaftOutcome Outcome, XDocument? File, string? Error)
{
    public static ExportSaftResult Generated(XDocument file) =>
        new(ExportSaftOutcome.Generated, file, null);

    public static ExportSaftResult Rejected(string error) =>
        new(ExportSaftOutcome.Rejected, null, error);
}

public enum ExportSaftOutcome
{
    Generated,
    Rejected,
}
