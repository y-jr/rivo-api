using System.Xml.Linq;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;

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
/// <strong>Estado: `Header` e todo o `MasterFiles` menos o plano de
/// contas.</strong> Clientes, fornecedores, produtos e tabela de impostos.
/// Falta `GeneralLedgerAccounts`, que é de `finance` e depende do PGC
/// angolano — que o ADR-037 recusou inventar. Os que vêm de outros módulos
/// entram pela porta <see cref="ISaftMasterData"/>; a tabela de impostos não,
/// porque é de `fiscal` e lê-se do seu próprio armazenamento.
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
    ITaxRateStore taxRates,
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

    /// <summary>
    /// <c>P</c> — Produtos. É o que todo o artigo de `inventory` é.
    ///
    /// <para>
    /// <strong>Não é uma omissão preguiçosa, é uma propriedade do
    /// agregado.</strong> <c>InventoryItem</c> exige unidade de medida, tem
    /// quantidade em mão, recebe-se, expede-se e transfere-se entre armazéns.
    /// Um serviço não faz nada disso — não há como registar um em `inventory`.
    /// </para>
    ///
    /// <para>
    /// Os outros valores da lista (<c>S</c> serviços, <c>O</c> outros, <c>E</c>
    /// impostos especiais de consumo, <c>I</c> outros impostos) chegam quando
    /// existir catálogo de serviços — que será outra fonte, não esta. Fixar
    /// aqui é honesto <em>enquanto</em> `inventory` for a única.
    /// </para>
    /// </summary>
    private const string ProdutoFisico = "P";

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

        var fornecedores = await masterData.ListSuppliersAsync(cancellationToken);

        var fornecedorRepetido = fornecedores
            .GroupBy(f => f.SupplierId, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (fornecedorRepetido is not null)
        {
            // `SupplierIDConstraint`, gémeo do dos clientes.
            return ExportSaftResult.Rejected(
                $"Há mais do que um fornecedor com o identificador '{fornecedorRepetido.Key}'. "
                + "O SAF-T exige que seja único no ficheiro.");
        }

        var artigos = await masterData.ListProductsAsync(cancellationToken);

        var artigoRepetido = artigos
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (artigoRepetido is not null)
        {
            // `ProductCodeConstraint`. A base de dados já tem índice único
            // sobre o SKU, mas a exportação não vê índices — e o dia em que a
            // fonte deixar de ser `inventory` esta verificação continua a ser
            // a que impede um ficheiro com dois artigos iguais.
            return ExportSaftResult.Rejected(
                $"Há mais do que um artigo com o código '{artigoRepetido.Key}'. "
                + "O SAF-T exige que seja único no ficheiro.");
        }

        var series = await taxRates.ListAsync(cancellationToken);

        var entradas = new List<XElement>();

        foreach (var serie in series.Where(EntraNaTabela))
        {
            if (!TaxCodes.IsSaftTaxCode(serie.Code))
            {
                // Recusar e não omitir. Uma série omitida sai do ficheiro em
                // silêncio, e as facturas que a referenciam passam a apontar
                // para um código que a tabela não declara — erro que só
                // aparece na AGT. A mensagem nomeia o código para quem o
                // corrigir saber qual é.
                return ExportSaftResult.Rejected(
                    $"O código de imposto '{serie.Code}' não é aceite pelo SAF-T. "
                    + "Os admitidos são NOR, RED, INT, ISE, OUT, NS, NA ou um número.");
            }

            entradas.AddRange(
                serie.Versions
                    .Where(v => Vigora(v, from, to))
                    .OrderBy(v => v.EffectiveFrom)
                    .Select(v => Imposto(serie, v)));
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
                new XElement(
                    Ns + "MasterFiles",
                    clientes.Select(Cliente),
                    fornecedores.Select(Fornecedor),
                    artigos.Select(Artigo),

                    // ⚠ Ausente e não vazio quando não há entradas — ao
                    // contrário de `MasterFiles`. `TaxTable` exige
                    // `minOccurs="1"` em `TaxTableEntry`, por isso um
                    // `<TaxTable/>` vazio torna o ficheiro inválido.
                    entradas.Count > 0
                        ? new XElement(Ns + "TaxTable", entradas)
                        : null)));

        return ExportSaftResult.Generated(documento);
    }

    /// <summary>
    /// Que séries entram na tabela de impostos do SAF-T.
    ///
    /// <para>
    /// <strong>Só o IVA.</strong> <c>TaxType</c> admite <c>IVA</c>, <c>IS</c>
    /// (imposto de selo) e <c>NS</c>, e mais nada. As séries de INSS que
    /// `payroll` usa não são imposto para este efeito — o próprio
    /// <see cref="TaxCodes.SocialSecurity"/> já o diz: «não vem do SAF-T».
    /// Enfiá-las na tabela seria declarar à AGT uma contribuição social como
    /// se fosse IVA.
    /// </para>
    /// </summary>
    private static bool EntraNaTabela(TaxRateSchedule serie) =>
        serie.Kind is TaxKind.ValueAdded;

    /// <summary>
    /// Se a versão da taxa esteve em vigor em algum momento do período.
    ///
    /// <para>
    /// Sobreposição de intervalos, e não "em vigor à data final": uma taxa que
    /// vigorou em Março e foi substituída em Junho tem de estar na tabela de
    /// um ficheiro anual, senão as facturas de Março referenciam-na sem ela lá
    /// estar. <c>EffectiveTo</c> é inclusivo e nulo na versão corrente.
    /// </para>
    /// </summary>
    private static bool Vigora(TaxRateVersion versao, DateOnly from, DateOnly to) =>
        versao.EffectiveFrom <= to && (versao.EffectiveTo is null || versao.EffectiveTo >= from);

    private XElement Imposto(TaxRateSchedule serie, TaxRateVersion versao)
    {
        var elementos = new List<XObject>
        {
            // `NS` é simultaneamente tipo e código no XSD: uma operação não
            // sujeita não é IVA a 0%, é outra coisa. Declarar `IVA`/`NS`
            // diria que houve imposto e foi zero.
            new XElement(
                Ns + "TaxType",
                string.Equals(serie.Code, TaxCodes.NotSubject, StringComparison.OrdinalIgnoreCase)
                    ? "NS"
                    : "IVA"),

            // Sem `TaxCountryRegion`: é opcional, e o valor que interessaria
            // distinguir — `AO-CAB`, o espaço fiscal de Cabinda — não existe
            // no modelo. Escrever `AO` a todas as séries seria afirmar que
            // nenhuma é de Cabinda, e isso não se sabe.
            new XElement(Ns + "TaxCode", serie.Code),
            new XElement(Ns + "Description", serie.Description),
        };

        if (versao.EffectiveTo is { } fim)
        {
            elementos.Add(new XElement(Ns + "TaxExpirationDate", fim.ToString("yyyy-MM-dd")));
        }

        // `TaxPercentage` e não `TaxAmount`: o XSD deixa escolher, e o Rivo só
        // modela percentagens (`TaxRateVersion.Percentage`).
        elementos.Add(new XElement(
            Ns + "TaxPercentage",
            versao.Percentage.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));

        return new XElement(Ns + "TaxTableEntry", elementos);
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
    /// Um elemento <c>Supplier</c>.
    ///
    /// <para>
    /// Quase igual a <see cref="Cliente"/> — os nomes dos dois primeiros
    /// elementos mudam, e a morada é <c>ShipFromAddress</c> em vez de
    /// <c>ShipToAddress</c> nos opcionais que não emitimos. Não se factorizou
    /// com o cliente porque o XSD os declara em separado, e o dia em que um
    /// deles ganhar um campo o outro não tem seria o dia em que a
    /// factorização passaria a mentir.
    /// </para>
    /// </summary>
    private XElement Fornecedor(SaftSupplier fornecedor) =>
        new(
            Ns + "Supplier",
            new XElement(Ns + "SupplierID", fornecedor.SupplierId),
            new XElement(Ns + "AccountID", ContaDesconhecida),
            new XElement(Ns + "SupplierTaxID", fornecedor.TaxId),
            new XElement(Ns + "CompanyName", fornecedor.Name),
            Morada(Ns + "BillingAddress", fornecedor.BillingAddress),
            new XElement(Ns + "SelfBillingIndicator", SemAutofacturacao));

    /// <summary>
    /// Um elemento <c>Product</c>.
    ///
    /// <para>
    /// <c>ProductNumberCode</c> repete <c>ProductCode</c>, e não é descuido: o
    /// XSD diz «deve ser usado o código EAN do produto. Quando este não
    /// existir, preencher com o valor do elemento <c>ProductCode</c>». O Rivo
    /// não modela EAN.
    /// </para>
    ///
    /// <para>
    /// Sem <c>ProductGroup</c> — é opcional, e `inventory` não tem famílias de
    /// artigo. Emitir uma inventada seria pior do que não emitir.
    /// </para>
    /// </summary>
    private XElement Artigo(SaftProduct artigo) =>
        new(
            Ns + "Product",
            new XElement(Ns + "ProductType", ProdutoFisico),
            new XElement(Ns + "ProductCode", artigo.Code),
            new XElement(Ns + "ProductDescription", artigo.Description),
            new XElement(Ns + "ProductNumberCode", artigo.Code));

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
