using System.Xml.Linq;
using Rivo.Fiscal.Application;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Application.UseCases;

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
        new ExportSaftFile(
            empresa ?? Empresa(),
            new MasterDataFalso(clientes),
            new FakeTimeProvider(Agora))
            .ExecuteAsync(fiscalYear, from, to, CancellationToken.None);

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

        await new ExportSaftFile(Empresa(), fonte, new FakeTimeProvider(Agora))
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
internal sealed class MasterDataFalso(IReadOnlyList<SaftCustomer> clientes) : ISaftMasterData
{
    public int Chamadas { get; private set; }

    public Task<IReadOnlyList<SaftCustomer>> ListCustomersAsync(CancellationToken cancellationToken)
    {
        Chamadas++;

        return Task.FromResult(clientes);
    }
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
