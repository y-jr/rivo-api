using System.Xml.Linq;

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
/// ficheiro inválido, e é a razão de esta primeira versão ser só o cabeçalho.
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
public sealed class ExportSaftFile(CompanyOptions company, TimeProvider clock)
{
    /// <summary>O espaço de nomes do SAF-T AO 1.01_01, tal como o XSD o fixa.</summary>
    public static readonly XNamespace Ns = "urn:OECD:StandardAuditFile-Tax:AO_1.01_01";

    public const string AuditFileVersion = "1.01_01";

    public ExportSaftResult Execute(int fiscalYear, DateOnly from, DateOnly to)
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

        var documento = new XDocument(
            new XDeclaration("1.0", "windows-1252", null),
            new XElement(
                Ns + "AuditFile",
                Header(fiscalYear, from, to),

                // Vazio e presente, e não ausente: o XSD declara `MasterFiles`
                // com `minOccurs` implícito de 1 e todos os filhos opcionais.
                // Um elemento vazio é o que diz "não há dados de referência
                // neste período", que é diferente de não dizer nada.
                new XElement(Ns + "MasterFiles")));

        return ExportSaftResult.Generated(documento);
    }

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
