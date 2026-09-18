using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Domain.Tests;

/// <summary>
/// A identidade fiscal da empresa (ADR-066).
///
/// <para>
/// Pouca regra, e é o esperado: isto é o <c>Header</c> do SAF-T, um conjunto de
/// campos que ou está preenchido ou não serve. O que há para testar é
/// exactamente onde o "não serve" é imposto.
/// </para>
/// </summary>
public sealed class TaxEntityProfileTests
{
    private static TaxEntityProfile Declarada() => TaxEntityProfile.Declare(
        "Rivo Testes, Lda.", "5000000000", "Rua Principal 1", "Luanda", "AO");

    [Fact]
    public void Declarar_FixaAChaveConhecida()
    {
        // Há uma identidade fiscal, não um catálogo delas (ADR-003, empresa
        // única). A chave constante é o que impede a segunda.
        Assert.Equal(TaxEntityProfile.WellKnownId, Declarada().Id);
    }

    [Theory]
    [InlineData("", "5000000000", "Rua Principal 1", "Luanda", "AO")]
    [InlineData("   ", "5000000000", "Rua Principal 1", "Luanda", "AO")]
    [InlineData("Rivo", "", "Rua Principal 1", "Luanda", "AO")]
    [InlineData("Rivo", "5000000000", "", "Luanda", "AO")]
    [InlineData("Rivo", "5000000000", "Rua Principal 1", "", "AO")]
    [InlineData("Rivo", "5000000000", "Rua Principal 1", "Luanda", "")]
    public void Declarar_SemUmDosObrigatorios_ERecusado(
        string nome, string nif, string endereco, string cidade, string pais) =>
        Assert.ThrowsAny<ArgumentException>(
            () => TaxEntityProfile.Declare(nome, nif, endereco, cidade, pais));

    [Fact]
    public void Declarar_AparaEspacosEmVolta()
    {
        var perfil = TaxEntityProfile.Declare(
            "  Rivo Testes, Lda.  ", " 5000000000 ", " Rua Principal 1 ", " Luanda ", " AO ");

        Assert.Equal("Rivo Testes, Lda.", perfil.CompanyName);
        Assert.Equal("5000000000", perfil.TaxRegistrationNumber);
        Assert.Equal("Rua Principal 1", perfil.AddressDetail);
        Assert.Equal("Luanda", perfil.City);
        Assert.Equal("AO", perfil.Country);
    }

    [Fact]
    public void Corrigir_SubstituiOsObrigatorios()
    {
        var perfil = Declarada();

        perfil.Correct("Rivo, S.A.", "5000000001", "Rua Nova 2", "Benguela", "AO");

        Assert.Equal("Rivo, S.A.", perfil.CompanyName);
        Assert.Equal("5000000001", perfil.TaxRegistrationNumber);
        Assert.Equal("Benguela", perfil.City);
    }

    [Fact]
    public void Corrigir_SemUmDosObrigatorios_ERecusadoESemDeixarOPerfilAMeio()
    {
        var perfil = Declarada();

        Assert.ThrowsAny<ArgumentException>(
            () => perfil.Correct("Rivo, S.A.", "", "Rua Nova 2", "Benguela", "AO"));

        // A validação acontece antes de qualquer atribuição. Se corresse a meio,
        // um pedido recusado deixava a razão social nova com o NIF antigo — e o
        // próximo documento saía com um cabeçalho que ninguém escreveu.
        Assert.Equal("Rivo Testes, Lda.", perfil.CompanyName);
        Assert.Equal("5000000000", perfil.TaxRegistrationNumber);
        Assert.Equal("Luanda", perfil.City);
    }

    /// <summary>
    /// Os opcionais aceitam vazio, e vazio quer dizer "não tenho" — não "erro".
    /// O compositor omite o que for nulo em vez de imprimir uma linha em branco.
    /// </summary>
    [Fact]
    public void Descrever_NormalizaVazioParaNulo()
    {
        var perfil = Declarada();

        perfil.Describe("  ", "", "   ", null, "");

        Assert.Null(perfil.BusinessName);
        Assert.Null(perfil.PostalCode);
        Assert.Null(perfil.Email);
        Assert.Null(perfil.Phone);
        Assert.Null(perfil.SoftwareValidationNumber);
    }

    [Fact]
    public void Descrever_GuardaOsOpcionaisQueVeem()
    {
        var perfil = Declarada();

        perfil.Describe("Rivo", "1000", "geral@rivo.ao", "+244 900 000 000", "12345/AGT/2026");

        Assert.Equal("Rivo", perfil.BusinessName);
        Assert.Equal("1000", perfil.PostalCode);
        Assert.Equal("geral@rivo.ao", perfil.Email);
        Assert.Equal("+244 900 000 000", perfil.Phone);
        Assert.Equal("12345/AGT/2026", perfil.SoftwareValidationNumber);
    }

    /// <summary>
    /// Uma segunda chamada a <c>Describe</c> com vazios limpa o que estava.
    /// É o comportamento certo para um formulário de configuração: o que o
    /// utilizador apagou fica apagado.
    /// </summary>
    [Fact]
    public void Descrever_DuasVezes_AUltimaManda()
    {
        var perfil = Declarada();

        perfil.Describe("Rivo", "1000", "geral@rivo.ao", null, "12345/AGT/2026");
        perfil.Describe(null, null, null, null, null);

        Assert.Null(perfil.BusinessName);
        Assert.Null(perfil.Email);
        Assert.Null(perfil.SoftwareValidationNumber);
    }
}
