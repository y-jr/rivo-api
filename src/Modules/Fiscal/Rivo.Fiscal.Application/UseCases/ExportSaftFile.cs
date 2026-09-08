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
/// <strong>Estado: `Header`, todo o `MasterFiles` menos o plano de contas, e
/// as facturas de venda em `SourceDocuments`.</strong> Falta
/// `GeneralLedgerAccounts` (de `finance`, depende do PGC angolano que o
/// ADR-037 recusou inventar), `GeneralLedgerEntries`, `MovementOfGoods`,
/// `Payments` e `PurchaseInvoices`.
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
/// <strong>A cadeia de assinatura existe</strong> (K7, ADR-060), e sai em
/// <c>Hash</c>. <c>HashControl</c> vai a <c>"0"</c> — o que o XSD manda usar
/// para software não validado. As duas coisas juntas são a afirmação correcta:
/// há chave de integridade, não há certificação.
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

    /// <summary>
    /// <c>O</c> — Outros. O tipo dos artigos que aparecem numa factura e não
    /// estão no catálogo de `inventory`.
    ///
    /// <para>
    /// <strong>Existem porque o ficheiro tem de fechar sobre si próprio.</strong>
    /// O SAF-T exige que cada <c>ProductCode</c> de uma linha apareça na tabela
    /// de produtos. As linhas de serviço não têm artigo em stock — e as
    /// anteriores a 2026-09-08 nem código tinham, ficaram com
    /// <c>"Desconhecido"</c>. Omiti-las deixaria facturas a apontar para
    /// produtos que o ficheiro não declara.
    /// </para>
    ///
    /// <para>
    /// <c>O</c> e não <c>S</c>: o XSD descreve <c>O</c> como «outros (ex.:
    /// portes debitados, adiantamentos recebidos ou alienação de activos)», que
    /// é a categoria dos itens facturados que não são artigo nem serviço
    /// declarado. Chamar-lhes <c>S</c> afirmaria que são serviços, e não se
    /// sabe. Quando existir catálogo de serviços, virão de lá com <c>S</c>.
    /// </para>
    /// </summary>
    private const string ProdutoNaoCatalogado = "O";

    /// <summary>
    /// <c>P</c> — documento produzido nesta aplicação. Os outros valores
    /// (<c>I</c> integrado de outra, <c>M</c> recuperação ou emissão manual)
    /// não se aplicam: o Rivo não importa documentos.
    /// </summary>
    private const string ProduzidoNaAplicacao = "P";

    /// <summary>
    /// O que vai em <c>Hash</c> e <c>SourceID</c> quando não há valor.
    ///
    /// <para>
    /// Os dois são obrigatórios no XSD, com <c>minLength</c> de 1, e as
    /// facturas anteriores à cadeia não têm nenhum (ADR-060). <c>"0"</c> é o
    /// que o XSD manda pôr em <c>HashControl</c> para software não validado, e
    /// diz aqui a mesma coisa: não há chave para este documento.
    /// </para>
    /// </summary>
    private const string SemValor = "0";

    /// <summary>
    /// O <c>CustomerID</c> das vendas a consumidor final.
    ///
    /// <para>
    /// Não há registo em `commercial` — a pessoa não se identificou — e o
    /// <c>CustomerID</c> é obrigatório em cada factura. Um identificador fixo
    /// junta todas essas vendas ao mesmo cliente do ficheiro, que é o que elas
    /// são: vendas a quem não se identificou.
    /// </para>
    ///
    /// <para>
    /// Público porque quem implementa a porta precisa dele — é lá que se sabe
    /// que uma factura não tem cliente registado.
    /// </para>
    /// </summary>
    public const string ConsumidorFinal = "CONSUMIDOR-FINAL";

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

        var facturas = await masterData.ListInvoicesAsync(from, to, cancellationToken);

        /*
         * ⚠ **Um documento emitido não se corrige, e isto não tem saída.**
         *
         * Desde 2026-09-08 uma série de IVA só se abre com um código que o
         * SAF-T aceita, e há `PATCH /fiscal/tax-rates/{id}/code` para as
         * anteriores. Mas a linha da factura **congela o código no momento da
         * emissão** — e tem de congelar, senão corrigir uma série reescreveria
         * o passado.
         *
         * A consequência é esta: um documento emitido com um código que a AGT
         * não aceita fica assim para sempre. Recusar é a única resposta
         * honesta — emiti-lo produziria um ficheiro que a AGT rejeita, e
         * omiti-lo produziria um ficheiro incompleto que também rejeita.
         */
        var notas = await masterData.ListCreditNotesAsync(from, to, cancellationToken);

        // Os documentos todos, para as verificações que não distinguem
        // factura de nota: código de imposto, artigo, cliente.
        var documentos = facturas.Concat(notas.Select(n => n.Document)).ToList();

        var linhaComCodigoInvalido = documentos
            .SelectMany(f => f.Lines.Select(l => (Factura: f, Linha: l)))
            .FirstOrDefault(x => !TaxCodes.IsSaftTaxCode(x.Linha.TaxCode));

        if (linhaComCodigoInvalido.Factura is not null)
        {
            return ExportSaftResult.Rejected(
                $"A factura {linhaComCodigoInvalido.Factura.Number} tem uma linha com o código "
                + $"de imposto '{linhaComCodigoInvalido.Linha.TaxCode}', que o SAF-T não aceita. "
                + "O código de uma linha fica congelado na emissão e não se corrige — "
                + "documentos emitidos antes da verificação de códigos não são exportáveis.");
        }

        // ⚠ **O ficheiro tem de fechar sobre si próprio.** Cada `ProductCode`
        // usado numa linha tem de existir na tabela de produtos, e as linhas de
        // serviço — mais as anteriores a haver código — não estão no catálogo
        // de `inventory`. Acrescentam-se aqui, com o tipo `O`.
        // O mesmo laço nos clientes: uma venda a consumidor final não tem
        // registo em `commercial`, e uma factura a apontar para um cliente que
        // o ficheiro não declara é uma referência pendurada. O cliente sai da
        // própria factura, onde ficou congelado na emissão.
        var clientesDeclarados = clientes
            .Select(c => c.CustomerId)
            .ToHashSet(StringComparer.Ordinal);

        var clientesDeFactura = documentos
            .Select(f => f.Customer)
            .Where(c => !clientesDeclarados.Contains(c.CustomerId))
            .GroupBy(c => c.CustomerId, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(c => c.CustomerId, StringComparer.Ordinal)
            .ToList();

        var codigosNoCatalogo = artigos
            .Select(a => a.Code)
            .ToHashSet(StringComparer.Ordinal);

        var acrescentados = documentos
            .SelectMany(f => f.Lines)
            .Where(l => !codigosNoCatalogo.Contains(l.ProductCode))
            .GroupBy(l => l.ProductCode, StringComparer.Ordinal)

            // A descrição da primeira linha que usou o código. Arbitrária
            // quando há várias, e é o melhor que há: o código não tem nome
            // próprio em lado nenhum.
            .Select(g => new SaftProduct(g.Key, g.First().Description))
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ToList();

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
                    clientesDeFactura.Select(Cliente),
                    fornecedores.Select(Fornecedor),
                    artigos.Select(a => Artigo(a, ProdutoFisico)),
                    acrescentados.Select(a => Artigo(a, ProdutoNaoCatalogado)),

                    // ⚠ Ausente e não vazio quando não há entradas — ao
                    // contrário de `MasterFiles`. `TaxTable` exige
                    // `minOccurs="1"` em `TaxTableEntry`, por isso um
                    // `<TaxTable/>` vazio torna o ficheiro inválido.
                    entradas.Count > 0
                        ? new XElement(Ns + "TaxTable", entradas)
                        : null),

                // Mesma regra do `TaxTable`: ausente quando não há documentos,
                // porque `SalesInvoices` exige `NumberOfEntries` e os totais.
                documentos.Count > 0
                    ? new XElement(Ns + "SourceDocuments", Vendas(facturas, notas))
                    : null));

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
    private XElement Artigo(SaftProduct artigo, string tipo) =>
        new(
            Ns + "Product",
            new XElement(Ns + "ProductType", tipo),
            new XElement(Ns + "ProductCode", artigo.Code),
            new XElement(Ns + "ProductDescription", artigo.Description),
            new XElement(Ns + "ProductNumberCode", artigo.Code));

    /// <summary>
    /// A secção <c>SalesInvoices</c>.
    ///
    /// <para>
    /// <strong>Os totais contam só as facturas não anuladas</strong>, e é o
    /// XSD que o manda: «deve conter a soma dos elementos DebitAmount dos
    /// documentos cujo o elemento InvoiceStatus seja igual a N». As anuladas
    /// continuam no ficheiro — o que não contam é para os totais.
    /// </para>
    ///
    /// <para>
    /// <c>TotalDebit</c> vai a zero porque uma factura de venda credita: cada
    /// linha sai como <c>CreditAmount</c>. Notas de débito, quando existirem,
    /// mudam isto.
    /// </para>
    /// </summary>
    private XElement Vendas(
        IReadOnlyList<SaftInvoice> facturas,
        IReadOnlyList<SaftCreditNote> notas)
    {
        return new XElement(
            Ns + "SalesInvoices",
            new XElement(Ns + "NumberOfEntries", facturas.Count + notas.Count),

            // ⚠ **Os dois totais não são simétricos por acaso.** Uma factura
            // credita e uma nota de crédito debita — é essa a diferença entre
            // os dois documentos, e é por isso que o XSD tem os dois campos.
            // Somar as notas ao crédito sobredeclararia a receita pelo dobro
            // do valor creditado.
            new XElement(
                Ns + "TotalDebit",
                Montante(notas
                    .Where(n => !n.Document.Cancelled)
                    .Sum(n => n.Document.NetTotal))),
            new XElement(
                Ns + "TotalCredit",
                Montante(facturas.Where(f => !f.Cancelled).Sum(f => f.NetTotal))),

            facturas.Select(f => Factura(f, null)),
            notas.Select(n => Factura(n.Document, n.CorrectedInvoiceNumber)));
    }

    /// <param name="facturaCorrigida">
    /// Preenchido só nas notas de crédito. Muda duas coisas: as linhas saem
    /// como <c>DebitAmount</c> em vez de <c>CreditAmount</c>, e cada uma leva
    /// a referência à factura que corrige — que o SAF-T exige quando o tipo é
    /// <c>NC</c>.
    /// </param>
    private XElement Factura(SaftInvoice factura, string? facturaCorrigida)
    {
        var elementos = new List<XObject>
        {
            new XElement(Ns + "InvoiceNo", factura.Number),

            new XElement(
                Ns + "DocumentStatus",
                new XElement(Ns + "InvoiceStatus", factura.Cancelled ? "A" : "N"),
                new XElement(Ns + "InvoiceStatusDate", Instante(factura.StatusDate)),

                // `Reason` só quando há uma. Escrever "—" numa factura viva
                // seria dizer que houve motivo para o estado dela.
                factura.Cancelled && !string.IsNullOrWhiteSpace(factura.CancellationReason)
                    ? new XElement(Ns + "Reason", factura.CancellationReason)
                    : null,
                new XElement(Ns + "SourceID", factura.IssuedBy ?? SemValor),
                new XElement(Ns + "SourceBilling", ProduzidoNaAplicacao)),

            // ⚠ O hash é o da cadeia do Rivo, e `HashControl` diz que não vem
            // de software validado (ADR-060). As duas coisas juntas são a
            // afirmação correcta: há chave de integridade, não há certificação.
            new XElement(Ns + "Hash", factura.Hash ?? SemValor),
            new XElement(Ns + "HashControl", SemValor),

            new XElement(Ns + "InvoiceDate", Data(factura.IssuedOn)),
            new XElement(Ns + "InvoiceType", factura.Type),

            // Nenhum dos três regimes se aplica ao Rivo: sem autofacturação,
            // sem regime de IVA de caixa, sem facturação por conta de
            // terceiros. A estrutura é obrigatória e os três valores são
            // verdade.
            new XElement(
                Ns + "SpecialRegimes",
                new XElement(Ns + "SelfBillingIndicator", SemAutofacturacao),
                new XElement(Ns + "CashVATSchemeIndicator", SemAutofacturacao),
                new XElement(Ns + "ThirdPartiesBillingIndicator", SemAutofacturacao)),

            new XElement(Ns + "SourceID", factura.IssuedBy ?? SemValor),
            new XElement(Ns + "SystemEntryDate", Instante(factura.SystemEntryDate)),
            new XElement(Ns + "CustomerID", factura.Customer.CustomerId),
        };

        elementos.AddRange(
            factura.Lines.Select(l => Linha(l, factura.TaxPointDate, facturaCorrigida)));

        elementos.Add(new XElement(
            Ns + "DocumentTotals",
            new XElement(Ns + "TaxPayable", Montante(factura.TaxTotal)),
            new XElement(Ns + "NetTotal", Montante(factura.NetTotal)),
            new XElement(Ns + "GrossTotal", Montante(factura.GrossTotal))));

        return new XElement(Ns + "Invoice", elementos);
    }

    /// <summary>
    /// Uma linha de documento.
    ///
    /// <para>
    /// <strong><c>ProductDescription</c> e <c>Description</c> levam o mesmo
    /// texto</strong>, e os dois são obrigatórios. O XSD quer a descrição do
    /// produto no primeiro e, no segundo, a que «consta do documento entregue
    /// ao cliente» — o Rivo só guarda uma, que é precisamente a do documento.
    /// Inventar uma descrição de produto diferente da que o cliente viu seria
    /// pior do que repetir.
    /// </para>
    ///
    /// <para>
    /// <c>CreditAmount</c> e não <c>DebitAmount</c>: uma venda credita.
    /// </para>
    /// </summary>
    private XElement Linha(
        SaftInvoiceLine linha, DateOnly taxPointDate, string? facturaCorrigida) =>
        new(
            Ns + "Line",
            new XElement(Ns + "LineNumber", linha.LineNumber),
            new XElement(Ns + "ProductCode", linha.ProductCode),
            new XElement(Ns + "ProductDescription", linha.Description),
            new XElement(Ns + "Quantity", Quantidade(linha.Quantity)),
            new XElement(Ns + "UnitOfMeasure", linha.UnitOfMeasure),
            new XElement(Ns + "UnitPrice", Montante(linha.UnitPrice)),
            new XElement(Ns + "TaxPointDate", Data(taxPointDate)),

            // `References` antes de `Description`: `xs:sequence`. O XSD marca-o
            // opcional, mas a documentacao diz que "o preenchimento e
            // obrigatorio quando o campo 4.1.4.8 for preenchido com NC".
            facturaCorrigida is null
                ? null
                : new XElement(
                    Ns + "References",
                    new XElement(Ns + "Reference", facturaCorrigida)),

            new XElement(Ns + "Description", linha.Description),

            // Uma nota de credito debita; uma factura credita.
            facturaCorrigida is null
                ? new XElement(Ns + "CreditAmount", Montante(linha.NetAmount))
                : new XElement(Ns + "DebitAmount", Montante(linha.NetAmount)),
            new XElement(
                Ns + "Tax",
                new XElement(
                    Ns + "TaxType",
                    string.Equals(linha.TaxCode, TaxCodes.NotSubject, StringComparison.OrdinalIgnoreCase)
                        ? "NS"
                        : "IVA"),
                new XElement(Ns + "TaxCode", linha.TaxCode),
                new XElement(Ns + "TaxPercentage", Percentagem(linha.TaxPercentage))));

    private static string Data(DateOnly data) =>
        data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>SAFdateTimeType</c> é <c>xs:dateTime</c>, e o "O" do .NET junta-lhe
    /// sete casas de fracção de segundo mais o deslocamento — válido, mas
    /// ilegível num ficheiro de auditoria. Segundos inteiros chegam.
    /// </summary>
    private static string Instante(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString(
            "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static string Montante(decimal valor) =>
        valor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string Quantidade(decimal valor) =>
        valor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static string Percentagem(decimal valor) =>
        valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

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

            // ⚠ **Os campos vazios viram "Desconhecido", e o país "AO".**
            // Não é laxismo: uma factura a consumidor final não tem morada de
            // propósito — `InvoicedParty` deixa-a vazia em vez de a inventar,
            // porque quem não se identifica também não dá morada. Mas o XSD
            // exige os três com `minLength` 1, e um ficheiro com
            // `<City/>` vazio é inválido.
            //
            // O domínio guarda a verdade; a exportação escolhe como a
            // escrever. "Desconhecido" é o termo que o XSD usa noutros campos
            // para o mesmo efeito.
            new XElement(Ns + "AddressDetail", OuDesconhecido(morada.Detail)),
            new XElement(Ns + "City", OuDesconhecido(morada.City)),

            // `Country` tem lista fechada e não admite "Desconhecido". `AO` é
            // a suposição declarada — a mesma que a migração dos fornecedores
            // fez, e pela mesma razão.
            new XElement(
                Ns + "Country",
                string.IsNullOrWhiteSpace(morada.Country) ? "AO" : morada.Country));

    private static string OuDesconhecido(string valor) =>
        string.IsNullOrWhiteSpace(valor) ? ContaDesconhecida : valor;

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
