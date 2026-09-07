using System.Xml.Linq;
using Rivo.Fiscal.Application;
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

    private static ExportSaftFile Exportador(CompanyOptions? empresa = null) =>
        new(empresa ?? Empresa(), new FakeTimeProvider(Agora));

    [Fact]
    public void FicheiroGerado_ValidaContraOXsd()
    {
        var resultado = Exportador().Execute(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(ExportSaftOutcome.Generated, resultado.Outcome);

        var erros = SaftSchema.Validar(resultado.File!);

        // A mensagem inclui os erros: um "esperava 0, obtive 3" sem dizer
        // quais obriga a reproduzir à mão para saber o que falhou.
        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public void FicheiroGerado_ComMoradaMinima_ValidaNaMesma()
    {
        // Sem rua nem número: `AddressDetail` cai em "Desconhecido", que é o
        // que impede o ficheiro de ser inválido por falta de um campo
        // obrigatório que ninguém configurou.
        var empresa = new CompanyOptions
        {
            Name = "Angotech, Lda.",
            TaxRegistrationNumber = "5417000000",
        };

        var resultado = Exportador(empresa).Execute(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        var erros = SaftSchema.Validar(resultado.File!);

        Assert.True(erros.Count == 0, string.Join("\n", erros));
    }

    [Fact]
    public void FicheiroGerado_ComTodosOsOpcionais_ValidaNaMesma()
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

        var erros = SaftSchema.Validar(
            Exportador(empresa).Execute(
                2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).File!);

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
    public void SoftwareValidationNumber_VaiAZero_PorqueNaoHaCertificacao()
    {
        var ficheiro = Exportador().Execute(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).File!;

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
    public void DataDeCriacao_EADeHoje_ENaoADoPeriodo()
    {
        var ficheiro = Exportador().Execute(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)).File!;

        var criado = ficheiro.Descendants(ExportSaftFile.Ns + "DateCreated").Single().Value;

        // É quando o ficheiro foi produzido, e não o período que descreve —
        // é isso que diz à AGT que extracção está a ver.
        Assert.Equal("2026-09-07", criado);
    }

    [Fact]
    public void PeriodoInvertido_ERecusado()
    {
        var resultado = Exportador().Execute(
            2026, new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1));

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
        Assert.Null(resultado.File);
    }

    [Fact]
    public void PeriodoQueAtravessaOAnoDeclarado_ERecusado()
    {
        // O XSD não o impede, mas um ficheiro que anuncia `FiscalYear` 2026 e
        // traz documentos de 2025 é incoerente consigo próprio.
        var resultado = Exportador().Execute(
            2026, new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(ExportSaftOutcome.Rejected, resultado.Outcome);
    }

    [Fact]
    public void SemIdentidadeDaEmpresa_ERecusadoComOsCamposEmFalta()
    {
        var resultado = new ExportSaftFile(
            new CompanyOptions(), new FakeTimeProvider(Agora)).Execute(
            2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

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
