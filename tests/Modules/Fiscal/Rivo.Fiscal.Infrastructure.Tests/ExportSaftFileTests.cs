using System.Xml.Linq;
using Rivo.Fiscal.Application;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Infrastructure.Tests;

/// <summary>
/// A exportação SAF-T, validada contra o XSD.
///
/// <para>
/// <strong>A asserção que importa é a validação, e não a inspecção de
/// campos.</strong> Um teste que confirmasse que o `CompanyName` está lá
/// passaria com um ficheiro que a AGT recusa por ordem de elementos errada —
/// e a ordem, no XSD, é significativa: <c>xs:sequence</c> e não
/// <c>xs:all</c>. Só o esquema apanha isso.
/// </para>
///
/// <para>
/// O contrato de completude manda isto explicitamente: «a exportação gerada
/// deve ser validada contra o XSD nos testes, não apenas em produção».
/// </para>
/// </summary>
public class ExportSaftFileTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static CompanyOptions Empresa() => new()
    {
        Name = "Angotech, Lda.",
        TaxRegistrationNumber = "5417000000",
        Address = new CompanyAddressOptions
        {
            StreetName = "Rua Rainha Ginga",
            BuildingNumber = "12",
            City = "Luanda",
            Province = "Luanda",
        },
    };

    private static Task<ExportSaftResult> Exportar(
        int fiscalYear,
        DateOnly from,
        DateOnly to,
        CompanyOptions? empresa = null,
        params SaftCustomer[] clientes) =>
        Exportar(fiscalYear, from, to, [], empresa, clientes);

    private static Task<ExportSaftResult> Exportar(
        int fiscalYear,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<TaxRateSchedule> taxas,
        CompanyOptions? empresa = null,
        params SaftCustomer[] clientes) =>
        new ExportSaftFile(
            empresa ?? Empresa(),
            new MasterDataFalso(clientes),
            new TaxRateStoreFalso(taxas),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(fiscalYear, from, to, CancellationToken.None);

    /// <summary>
    /// Uma série de IVA com uma versão. Usa o agregado a sério e não um duplo:
    /// as invariantes de vigência são dele, e um duplo do agregado permitiria
    /// construir estados que a aplicação nunca produz.
    /// </summary>
    internal static TaxRateSchedule Taxa(
        string codigo = "NOR",
        decimal percentagem = 14m,
        DateOnly? de = null,
        DateOnly? ate = null,
        TaxKind tipo = TaxKind.ValueAdded)
    {
        var serie = TaxRateSchedule.Open(tipo, codigo, $"Descrição de {codigo}");

        serie.Introduce(
            percentagem,
            de ?? new DateOnly(2026, 1, 1),
            ate,
            "Lei n.º 7/19");

        return serie;
    }

    /// <summary>Um cliente completo, para os casos que não são sobre o cliente.</summary>
    internal static SaftCustomer Cliente(
        string id = "CLI-1",
        string nif = "5417000001",
        string nome = "Padaria Kilamba, Lda.") =>
        new(id, nif, nome, new SaftAddress("Rua 21 de Janeiro, 4", "Luanda", "AO"));

    [Fact]
    public async Task FicheiroGerado_ValidaContraOXsd()
    {
        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        // A mensagem inclui os erros: um "esperava 0, obtive 3" sem dizer
        // quais obriga a reproduzir à mão para saber o que falhou.
        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task ComClientes_ValidaContraOXsd()
    {
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            empresa: null,
            Cliente("CLI-1", "5417000001", "Padaria Kilamba, Lda."),
            Cliente("CLI-2", "5417000002", "Farmácia Talatona"));

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var emitidos = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Customer")
            .Select(c => c.Element(ExportSaftFile.Ns + "CustomerID")!.Value)
            .ToList();

        Assert.Equal(["CLI-1", "CLI-2"], emitidos);
    }

    [Fact]
    public async Task ClienteSemContaCorrente_VaiComoDesconhecido()
    {
        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            empresa: null,
            Cliente());

        var conta = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Customer")
            .Single()
            .Element(ExportSaftFile.Ns + "AccountID")!.Value;

        // O XSD obriga a `AccountID` e prevê literalmente "Desconhecido" para
        // quem não tem plano de contas. O ADR-037 recusou inventar o PGC
        // angolano — este caso fixa que a saída é a prevista e não um código
        // improvisado que pareceria uma conta a sério.
        Assert.Equal("Desconhecido", conta);
    }

    [Fact]
    public async Task ClientesComOMesmoIdentificador_ERecusado()
    {
        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            empresa: null,
            Cliente("CLI-1", "5417000001", "Padaria Kilamba, Lda."),
            Cliente("CLI-1", "5417000002", "Farmácia Talatona"));

        // `CustomerIDConstraint` no XSD. Recusa-se aqui para a mensagem
        // nomear o cliente, em vez de sair um erro de chave duplicada que não
        // diz a quem exporta o que corrigir.
        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Contains("CLI-1", resultado.Error);
    }

    [Fact]
    public async Task ComFornecedores_ValidaContraOXsd()
    {
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [Cliente()],
                [
                    new SaftSupplier("FOR-1", "5417100001", "Angoferragens, Lda.",
                        new SaftAddress("Rua Che Guevara, 88", "Luanda", "AO")),
                    new SaftSupplier("FOR-2", "5417100002", "Global Parts BV",
                        new SaftAddress("Keizersgracht 5", "Amesterdão", "NL")),
                ]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        // ⚠ `Customer` antes de `Supplier`: o XSD usa `xs:sequence` dentro de
        // `MasterFiles`, tal como dentro de cada elemento. Emitir os
        // fornecedores primeiro invalidaria o ficheiro.
        var ordem = resultado.File!
            .Descendants(ExportSaftFile.Ns + "MasterFiles")
            .Single()
            .Elements()
            .Select(e => e.Name.LocalName)
            .Distinct()
            .ToList();

        Assert.Equal(["Customer", "Supplier"], ordem);
    }

    [Fact]
    public async Task FornecedorEstrangeiro_SaiComOPaisDele()
    {
        // A morada do fornecedor é `SupplierAddressStructure`, cujo `Country`
        // tem lista fechada de países no XSD — ao contrário da do cliente.
        // Fixar `AO` aqui teria passado nos testes e mentido no ficheiro.
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [],
                [
                    new SaftSupplier("FOR-2", "5417100002", "Global Parts BV",
                        new SaftAddress("Keizersgracht 5", "Amesterdão", "NL")),
                ]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        var pais = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Supplier")
            .Single()
            .Element(ExportSaftFile.Ns + "BillingAddress")!
            .Element(ExportSaftFile.Ns + "Country")!.Value;

        Assert.Equal("NL", pais);
    }

    [Fact]
    public async Task FornecedoresComOMesmoIdentificador_ERecusado()
    {
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [],
                [
                    new SaftSupplier("FOR-1", "5417100001", "Angoferragens",
                        new SaftAddress("Rua A", "Luanda", "AO")),
                    new SaftSupplier("FOR-1", "5417100002", "Outra",
                        new SaftAddress("Rua B", "Luanda", "AO")),
                ]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        // `SupplierIDConstraint`, gémeo do dos clientes.
        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Contains("FOR-1", resultado.Error);
    }

    [Fact]
    public async Task ComArtigos_ValidaContraOXsd()
    {
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [Cliente()],
                [],
                [
                    new SaftProduct("CIM-42", "Cimento Portland 50 kg"),
                    new SaftProduct("VAR-10", "Vergalhão 10 mm"),
                ]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var artigo = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Product")
            .First();

        // Sem EAN no modelo, `ProductNumberCode` repete `ProductCode` — é o
        // que o XSD manda fazer nesse caso, e não um valor a inventar.
        Assert.Equal("CIM-42", artigo.Element(ExportSaftFile.Ns + "ProductCode")!.Value);
        Assert.Equal("CIM-42", artigo.Element(ExportSaftFile.Ns + "ProductNumberCode")!.Value);
        Assert.Equal("P", artigo.Element(ExportSaftFile.Ns + "ProductType")!.Value);
    }

    [Fact]
    public async Task ArtigosComOMesmoCodigo_ERecusado()
    {
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [],
                [],
                [
                    new SaftProduct("CIM-42", "Cimento Portland 50 kg"),
                    new SaftProduct("CIM-42", "Cimento, outra vez"),
                ]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        // `ProductCodeConstraint`. O SKU já tem índice único na base de dados,
        // mas a exportação não vê índices.
        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Contains("CIM-42", resultado.Error);
    }

    [Fact]
    public async Task AOrdemDoMasterFilesRespeitaOXsd()
    {
        // ⚠ `xs:sequence`: Customer, Supplier, Product, TaxTable. Trocar
        // qualquer par invalida o ficheiro, e nenhum teste de secção isolada
        // apanharia isso — só um com as quatro ao mesmo tempo.
        var resultado = await new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                [Cliente()],
                [
                    new SaftSupplier("FOR-1", "5417100001", "Angoferragens",
                        new SaftAddress("Rua A", "Luanda", "AO")),
                ],
                [new SaftProduct("CIM-42", "Cimento Portland 50 kg")]),
            new TaxRateStoreFalso([Taxa("NOR", 14m)]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var ordem = resultado.File!
            .Descendants(ExportSaftFile.Ns + "MasterFiles")
            .Single()
            .Elements()
            .Select(e => e.Name.LocalName)
            .ToList();

        Assert.Equal(["Customer", "Supplier", "Product", "TaxTable"], ordem);
    }

    private static SaftInvoice Factura(
        string numero = "FT S001/1",
        bool anulada = false,
        string clienteId = "CLI-1",
        string codigoDeArtigo = "CIM-42",
        string? hash = "elo-1",
        decimal liquido = 100_000m) =>
        new(
            numero,
            "FT",
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 3, 15),
            new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.Zero),
            anulada,
            anulada ? "Emitida ao cliente errado" : null,
            new SaftCustomer(
                clienteId, "5417000001", "Padaria Kilamba, Lda.",
                new SaftAddress("Rua 21 de Janeiro, 4", "Luanda", "AO")),
            "UTIL-1",
            hash,
            liquido,
            liquido * 0.14m,
            liquido * 1.14m,
            [
                new SaftInvoiceLine(
                    1, codigoDeArtigo, "Cimento Portland 50 kg", 4, "sc",
                    liquido / 4, liquido, "NOR", 14m),
            ]);

    private static Task<ExportSaftResult> ExportarComFacturas(
        IReadOnlyList<SaftInvoice> facturas,
        IReadOnlyList<SaftCustomer>? clientes = null,
        IReadOnlyList<SaftProduct>? artigos = null) =>
        new ExportSaftFile(
            Empresa(),
            new MasterDataFalso(
                clientes ?? [Cliente()],
                [],
                artigos ?? [new SaftProduct("CIM-42", "Cimento Portland 50 kg")],
                facturas),
            new TaxRateStoreFalso([Taxa("NOR", 14m)]),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

    [Fact]
    public async Task ComFacturas_ValidaContraOXsd()
    {
        var resultado = await ExportarComFacturas([Factura()]);

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task OsTotaisContamSoAsFacturasNaoAnuladas()
    {
        // O XSD é explícito: «deve conter a soma dos elementos DebitAmount dos
        // documentos cujo o elemento InvoiceStatus seja igual a N». A anulada
        // continua no ficheiro; o que não faz é somar.
        var resultado = await ExportarComFacturas(
            [
                Factura("FT S001/1", liquido: 100_000m),
                Factura("FT S001/2", anulada: true, liquido: 500_000m),
            ]);

        var vendas = resultado.File!.Descendants(ExportSaftFile.Ns + "SalesInvoices").Single();

        // As duas entram na contagem — e só uma no total.
        Assert.Equal("2", vendas.Element(ExportSaftFile.Ns + "NumberOfEntries")!.Value);
        Assert.Equal("100000.00", vendas.Element(ExportSaftFile.Ns + "TotalCredit")!.Value);

        Assert.Equal(
            ["N", "A"],
            resultado.File!
                .Descendants(ExportSaftFile.Ns + "InvoiceStatus")
                .Select(e => e.Value));
    }

    [Fact]
    public async Task ArtigoFacturadoQueNaoEstaNoCatalogo_EntraNaTabelaDeProdutos()
    {
        // ⚠ Sem isto o ficheiro tem uma referência pendurada: a linha aponta
        // para um `ProductCode` que a tabela de produtos não declara. Acontece
        // com qualquer linha de serviço — serviços não estão em `inventory`.
        var resultado = await ExportarComFacturas(
            [Factura(codigoDeArtigo: "CONSULTORIA")],
            artigos: [new SaftProduct("CIM-42", "Cimento Portland 50 kg")]);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var artigos = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Product")
            .ToDictionary(
                p => p.Element(ExportSaftFile.Ns + "ProductCode")!.Value,
                p => p.Element(ExportSaftFile.Ns + "ProductType")!.Value);

        Assert.Equal("P", artigos["CIM-42"]);

        // `O` — Outros. Não `S`: afirmar que é serviço seria dizer o que não
        // se sabe.
        Assert.Equal("O", artigos["CONSULTORIA"]);
    }

    [Fact]
    public async Task ConsumidorFinal_EntraNaTabelaDeClientes()
    {
        // Uma venda a quem não se identificou não tem registo em `commercial`,
        // e o `CustomerID` é obrigatório na factura. O cliente sai da própria
        // factura, onde ficou congelado na emissão.
        var resultado = await ExportarComFacturas(
            [Factura(clienteId: ExportSaftFile.ConsumidorFinal)],
            clientes: []);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var declarado = Assert.Single(
            resultado.File!.Descendants(ExportSaftFile.Ns + "Customer"));

        Assert.Equal(
            ExportSaftFile.ConsumidorFinal,
            declarado.Element(ExportSaftFile.Ns + "CustomerID")!.Value);
    }

    [Fact]
    public async Task ClienteJaNaTabela_NaoEDuplicado()
    {
        // ⚠ `CustomerIDConstraint`. Se o laço acrescentasse o cliente da
        // factura sem verificar, o ficheiro ficava com dois iguais e inválido.
        var resultado = await ExportarComFacturas(
            [Factura(clienteId: "CLI-1")],
            clientes: [Cliente("CLI-1")]);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        Assert.Single(resultado.File!.Descendants(ExportSaftFile.Ns + "Customer"));
    }

    [Fact]
    public async Task FacturaAnteriorACadeia_SaiComHashZero()
    {
        // `Hash` é obrigatório com `minLength` 1, e as facturas anteriores à
        // cadeia não têm nenhum (ADR-060). "0" diz o mesmo que `HashControl`
        // já diz do ficheiro inteiro: não há chave para este documento.
        var resultado = await ExportarComFacturas([Factura(hash: null)]);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        Assert.Equal("0", resultado.File!.Descendants(ExportSaftFile.Ns + "Hash").Single().Value);
    }

    [Fact]
    public async Task ConsumidorFinalSemMorada_SaiComoDesconhecido()
    {
        // ⚠ Apanhado contra dados reais, não em teste. `InvoicedParty` deixa a
        // morada do consumidor final **vazia**, de propósito — quem não se
        // identifica também não dá morada. O XSD exige os três campos com
        // `minLength` 1, e o ficheiro saía inválido.
        var semMorada = Factura(clienteId: ExportSaftFile.ConsumidorFinal) with
        {
            Customer = new SaftCustomer(
                ExportSaftFile.ConsumidorFinal, "999999999", "Consumidor Final",
                new SaftAddress(string.Empty, string.Empty, string.Empty)),
        };

        var resultado = await ExportarComFacturas([semMorada], clientes: []);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var morada = resultado.File!
            .Descendants(ExportSaftFile.Ns + "Customer")
            .Single()
            .Element(ExportSaftFile.Ns + "BillingAddress")!;

        Assert.Equal("Desconhecido", morada.Element(ExportSaftFile.Ns + "City")!.Value);
        Assert.Equal("AO", morada.Element(ExportSaftFile.Ns + "Country")!.Value);
    }

    [Fact]
    public async Task FacturaComCodigoDeImpostoQueOXsdNaoAceita_ERecusada()
    {
        // ⚠ Apanhado contra dados reais. Corrigir o código de uma série **não**
        // corrige as facturas já emitidas com ele — a linha congela-o na
        // emissão, e tem de congelar. Recusar é a única resposta honesta:
        // emitir daria um ficheiro que a AGT rejeita, omitir daria um
        // incompleto que também rejeita.
        var comCodigoAntigo = Factura() with
        {
            Lines =
            [
                new SaftInvoiceLine(
                    1, "CIM-42", "Cimento", 1, "sc", 100m, 100m, "F561883", 14m),
            ],
        };

        var resultado = await ExportarComFacturas([comCodigoAntigo]);

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Contains("F561883", resultado.Error);
        Assert.Contains("FT S001/1", resultado.Error);
    }

    [Fact]
    public async Task SemFacturas_ASeccaoNaoAparece()
    {
        // Mesma regra do `TaxTable`: `SalesInvoices` exige `NumberOfEntries` e
        // os totais, por isso um elemento vazio invalidaria o ficheiro.
        var resultado = await ExportarComFacturas([]);

        Assert.Empty(resultado.File!.Descendants(ExportSaftFile.Ns + "SourceDocuments"));

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task ComTabelaDeImpostos_ValidaContraOXsd()
    {
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            [Taxa("NOR", 14m), Taxa("ISE", 0m)]);

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));

        var codigos = resultado.File!
            .Descendants(ExportSaftFile.Ns + "TaxTableEntry")
            .Select(e => e.Element(ExportSaftFile.Ns + "TaxCode")!.Value)
            .ToList();

        Assert.Equal(["NOR", "ISE"], codigos);
    }

    [Fact]
    public async Task SemTaxas_ATabelaNaoAparece()
    {
        // ⚠ Ausente, não vazia. `TaxTable` exige `minOccurs="1"` em
        // `TaxTableEntry` — o inverso de `MasterFiles`, que sai vazio. Um
        // `<TaxTable/>` sem filhos invalida o ficheiro.
        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []);

        Assert.Empty(resultado.File!.Descendants(ExportSaftFile.Ns + "TaxTable"));

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task InssNaoEntraNaTabelaDeImpostos()
    {
        // `TaxType` só admite IVA, IS e NS. O INSS é contribuição social, e o
        // próprio `TaxCodes.SocialSecurity` diz que o código não vem do SAF-T.
        // Declará-lo aqui seria dizer à AGT que é IVA.
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            [
                Taxa("NOR", 14m),
                Taxa("INSS", 3m, tipo: TaxKind.EmployeeSocialSecurity),
            ]);

        var codigos = resultado.File!
            .Descendants(ExportSaftFile.Ns + "TaxCode")
            .Select(e => e.Value)
            .ToList();

        Assert.Equal(["NOR"], codigos);
    }

    [Fact]
    public async Task TaxaQueVigorouENoMeioDoPeriodo_EntraNaTabela()
    {
        // Substituída em Junho. Tem de estar no ficheiro anual: as facturas de
        // Março referenciam-na, e a tabela é o que lhes dá significado.
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            [Taxa("NOR", 14m, de: new DateOnly(2025, 1, 1), ate: new DateOnly(2026, 5, 31))]);

        var entrada = Assert.Single(
            resultado.File!.Descendants(ExportSaftFile.Ns + "TaxTableEntry"));

        Assert.Equal(
            "2026-05-31",
            entrada.Element(ExportSaftFile.Ns + "TaxExpirationDate")!.Value);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task TaxaQueCaducouAntesDoPeriodo_FicaDeFora()
    {
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            [Taxa("NOR", 10m, de: new DateOnly(2024, 1, 1), ate: new DateOnly(2025, 12, 31))]);

        Assert.Empty(resultado.File!.Descendants(ExportSaftFile.Ns + "TaxTableEntry"));
    }

    [Fact]
    public async Task CodigoDeImpostoQueOXsdNaoAceita_ERecusado()
    {
        // ⚠ A série tem de ser adulterada por reflexão porque
        // `TaxRateSchedule.Open` já a recusa — e é esse o ponto. A verificação
        // na exportação continua a existir para as linhas gravadas **antes**
        // dessa regra, que o EF materializa pelo construtor privado sem passar
        // pela fábrica. Sem elas o beco não existiria; com elas, existe.
        var legado = Taxa("NOR", 14m);

        typeof(TaxRateSchedule)
            .GetProperty(nameof(TaxRateSchedule.Code))!
            .SetValue(legado, "NORMAL");

        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [legado]);

        // Recusar nomeando o código é melhor do que omitir a série em silêncio
        // e deixar as facturas a apontar para uma taxa que a tabela não declara.
        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Contains("NORMAL", resultado.Error);
    }

    [Fact]
    public async Task NaoSujeito_VaiComTaxTypeNs_ENaoIva()
    {
        // Uma operação não sujeita não é IVA a 0%. `IVA`/`NS` diria que houve
        // imposto e foi zero — que é outra afirmação.
        var resultado = await Exportar(
            2026,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            [Taxa("NS", 0m)]);

        var entrada = Assert.Single(
            resultado.File!.Descendants(ExportSaftFile.Ns + "TaxTableEntry"));

        Assert.Equal("NS", entrada.Element(ExportSaftFile.Ns + "TaxType")!.Value);
    }

    [Fact]
    public async Task FicheiroGerado_ComMoradaMinima_ValidaNaMesma()
    {
        // Sem rua nem número: `AddressDetail` cai em "Desconhecido", que é o
        // que impede o ficheiro de ser inválido por falta de um campo
        // obrigatório que ninguém configurou.
        var empresa = new CompanyOptions
        {
            Name = "Angotech, Lda.",
            TaxRegistrationNumber = "5417000000",
        };

        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), empresa);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public async Task FicheiroGerado_ComTodosOsOpcionais_ValidaNaMesma()
    {
        var empresa = new CompanyOptions
        {
            Name = "Angotech, Lda.",
            BusinessName = "Angotech",
            TaxRegistrationNumber = "5417000000",
            Telephone = "222000000",
            Email = "geral@angotech.ao",
            Website = "https://angotech.ao",
            Address = new CompanyAddressOptions
            {
                StreetName = "Rua Rainha Ginga",
                BuildingNumber = "12",
                AddressDetail = "Rua Rainha Ginga 12, 3.º andar",
                City = "Luanda",
                Province = "Luanda",
            },
        };

        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), empresa);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public void OEsquemaApanhaUmFicheiroPartido()
    {
        // ⚠ Sem este caso, os três acima não provariam nada: um `SaftSchema`
        // que devolvesse sempre lista vazia passaria em todos. Este confirma
        // que a validação **recusa** — e é o que dá valor às outras.
        var partido = new XDocument(
            new XElement(ExportSaftFile.Ns + "AuditFile",
                new XElement(ExportSaftFile.Ns + "Header",
                    new XElement(ExportSaftFile.Ns + "CompanyName", "Sem o resto"))));

        var erros = SaftSchema.Validar(partido);

        Assert.NotEmpty(erros);
    }

    [Fact]
    public async Task SoftwareValidationNumber_VaiAZero_PorqueNaoHaCertificacao()
    {
        var ficheiro = (await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).File!;

        var numero = ficheiro
            .Descendants(ExportSaftFile.Ns + "SoftwareValidationNumber")
            .Single().Value;

        // "0" é o que o XSD admite para software não validado, e é verdade:
        // o Rivo não está certificado pela AGT (ADR-036). Se algum dia isto
        // deixar de ser "0" por omissão, é porque alguém a obteve — e este
        // caso obriga a que essa mudança seja deliberada.
        Assert.Equal("0", numero);
    }

    [Fact]
    public async Task DataDeCriacao_EADeHoje_ENaoADoPeriodo()
    {
        var ficheiro = (await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31))).File!;

        var criado = ficheiro.Descendants(ExportSaftFile.Ns + "DateCreated").Single().Value;

        // É quando o ficheiro foi produzido, e não o período que descreve —
        // é isso que diz à AGT que extracção está a ver.
        Assert.Equal("2026-09-07", criado);
    }

    [Fact]
    public async Task PeriodoInvertido_ERecusado()
    {
        var resultado = await Exportar(
            2026, new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1));

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Null(resultado.File);
    }

    [Fact]
    public async Task PeriodoQueAtravessaOAnoDeclarado_ERecusado()
    {
        // O XSD não o impede, mas um ficheiro que anuncia `FiscalYear` 2026 e
        // traz documentos de 2025 é incoerente consigo próprio.
        var resultado = await Exportar(
            2026, new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
    }

    [Fact]
    public async Task PeriodoInvalido_NemChegaAPedirOsClientes()
    {
        // Ordem, não estética: ler a carteira inteira para depois recusar por
        // datas invertidas é trabalho deitado fora — e num SAF-T a carteira
        // pode ser grande.
        var fonte = new MasterDataFalso([]);

        await new ExportSaftFile(Empresa(), fonte, new TaxRateStoreFalso([]), new FakeTimeProvider(Agora))
            .ExecuteAsync(
                2026,
                new DateOnly(2026, 12, 31),
                new DateOnly(2026, 1, 1),
                CancellationToken.None);

        Assert.Equal(0, fonte.Chamadas);
    }

    [Fact]
    public async Task SemIdentidadeDaEmpresa_ERecusadoComOsCamposEmFalta()
    {
        var resultado = await Exportar(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            empresa: new CompanyOptions());

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);

        // A mensagem nomeia os campos: quem a lê tem de saber o que pôr no
        // `.env`, e "identidade incompleta" sozinho não chega.
        Assert.Contains("Company:Name", resultado.Error);
        Assert.Contains("Company:TaxRegistrationNumber", resultado.Error);
    }
}

/// <summary>Relógio fixo, escrito à mão — ADR-022.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

/// <summary>
/// A porta de relato, escrita à mão — ADR-022.
///
/// <para>
/// Conta as chamadas porque uma delas é uma asserção: a exportação não deve
/// pedir a carteira de clientes antes de saber que o pedido é válido.
/// </para>
/// </summary>
internal sealed class MasterDataFalso(
    IReadOnlyList<SaftCustomer> clientes,
    IReadOnlyList<SaftSupplier>? fornecedores = null,
    IReadOnlyList<SaftProduct>? artigos = null,
    IReadOnlyList<SaftInvoice>? facturas = null) : ISaftMasterData
{
    public Task<IReadOnlyList<SaftInvoice>> ListInvoicesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        Chamadas++;

        return Task.FromResult<IReadOnlyList<SaftInvoice>>(facturas ?? []);
    }

    public Task<IReadOnlyList<SaftProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        Chamadas++;

        return Task.FromResult<IReadOnlyList<SaftProduct>>(artigos ?? []);
    }

    public int Chamadas { get; private set; }

    public Task<IReadOnlyList<SaftCustomer>> ListCustomersAsync(CancellationToken cancellationToken)
    {
        Chamadas++;

        return Task.FromResult(clientes);
    }

    public Task<IReadOnlyList<SaftSupplier>> ListSuppliersAsync(CancellationToken cancellationToken)
    {
        Chamadas++;

        return Task.FromResult<IReadOnlyList<SaftSupplier>>(fornecedores ?? []);
    }
}

/// <summary>
/// O armazenamento de taxas, escrito à mão — ADR-022.
///
/// <para>
/// Só <c>ListAsync</c> tem comportamento. Os outros métodos rebentam em vez de
/// devolverem nulo: se a exportação alguma vez os chamar, quero saber, e um
/// <c>null</c> silencioso faria o teste passar com a exportação a fazer outra
/// coisa.
/// </para>
/// </summary>
internal sealed class TaxRateStoreFalso(IReadOnlyList<TaxRateSchedule> series) : ITaxRateStore
{
    public Task<IReadOnlyList<TaxRateSchedule>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(series);

    public Task<TaxRateSchedule?> FindAsync(TaxKind kind, string code, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A exportação não procura séries uma a uma.");

    public Task<TaxRateSchedule?> FindByIdAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A exportação não procura séries uma a uma.");

    public Task AddAsync(TaxRateSchedule schedule, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A exportação não escreve.");

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("A exportação não escreve.");
}

/// <summary>
/// O comprimento do NIF.
///
/// <para>
/// O XSD exige 10 a 15 caracteres (<c>SAFAOAngolaVatNumber</c>), e a
/// verificação de arranque só confirmava que o campo não estava vazio. Um NIF
/// de nove dígitos levantava a aplicação sem uma queixa e produzia depois um
/// ficheiro que a AGT recusa — descoberto no dia da entrega, que é
/// exactamente o que o ADR-058 dizia querer evitar.
/// </para>
/// </summary>
public class NifCurtoTests
{
    private static CompanyOptions ComNif(string nif) => new()
    {
        Name = "Angotech, Lda.",
        TaxRegistrationNumber = nif,
    };

    [Theory]
    [InlineData("541700000")]      // 9 — um a menos do que o mínimo
    [InlineData("1")]
    [InlineData("5417000000000000")] // 16 — um a mais do que o máximo
    public void NifForaDoComprimento_ERecusadoNoArranque(string nif)
    {
        var problemas = ComNif(nif).CamposEmFalta();

        // ⚠ Sem a verificação de comprimento, isto vem vazio — e é esse o
        // defeito. O caso existe para o fixar.
        Assert.NotEmpty(problemas);
        Assert.Contains(problemas, p => p.Contains("TaxRegistrationNumber"));
    }

    [Theory]
    [InlineData("5417000000")]       // 10 — o mínimo exacto
    [InlineData("541700000000000")]  // 15 — o máximo exacto
    public void NifNosLimites_EAceite(string nif)
    {
        Assert.Empty(ComNif(nif).CamposEmFalta());
    }

    [Fact]
    public async Task NifNoMinimo_ProduzFicheiroQueValida()
    {
        // As duas metades têm de concordar: o que a verificação aceita, o XSD
        // também tem de aceitar. Um limite errado num dos lados seria pior do
        // que não ter limite nenhum.
        var resultado = await new ExportSaftFile(
            ComNif("5417000000"),
            new MasterDataFalso([]),
            new TaxRateStoreFalso([]),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)))
            .ExecuteAsync(
                2026,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31),
                CancellationToken.None);

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }
}
