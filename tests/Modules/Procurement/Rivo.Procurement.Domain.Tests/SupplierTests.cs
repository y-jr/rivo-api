using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Domain.Tests;

public class SupplierTests
{
    // Calculado pela própria norma ISO 13616 e confirmado à parte: os quatro
    // primeiros caracteres passam para o fim, `AO` vira `1024`, e o resultado
    // tem de dar resto 1 módulo 97.
    private const string IbanValido = "AO71000600000109131234151";

    private static SupplierAddress Morada() =>
        new("Rua Rainha Ginga, 12", "Luanda", "AO");

    [Fact]
    public void Register_SemMorada_ERecusado()
    {
        // Obrigatória porque o SAF-T a exige em `Supplier.BillingAddress`, sem
        // `minOccurs="0"`. O que não se captura no registo não se reconstrói
        // no dia da entrega à AGT.
        Assert.Throws<ArgumentNullException>(
            () => Supplier.Register("Angoferragens", "5417123456", null!));
    }

    [Theory]
    [InlineData("", "Luanda", "AO")]
    [InlineData("   ", "Luanda", "AO")]
    [InlineData("Rua A", "", "AO")]
    [InlineData("Rua A", "Luanda", "")]
    [InlineData("Rua A", "Luanda", "Angola")]
    [InlineData("Rua A", "Luanda", "A")]
    public void MoradaIncompleta_ERecusada(string detalhe, string cidade, string pais)
    {
        Assert.Throws<ArgumentException>(() => new SupplierAddress(detalhe, cidade, pais));
    }

    [Fact]
    public void OPaisENormalizadoParaMaiusculas()
    {
        // O XSD tem lista fechada de países em maiúsculas
        // (`SupplierCountry`); `ao` não passaria.
        var morada = new SupplierAddress("Rua A", "Luanda", "ao");

        Assert.Equal("AO", morada.Country);
    }

    [Fact]
    public void MudarAMorada_SubstituiInteira()
    {
        // Objecto de valor: substitui-se, não se edita campo a campo.
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.ChangeBillingAddress(new SupplierAddress("Rua Nova, 3", "Benguela", "AO"));

        Assert.Equal("Benguela", fornecedor.BillingAddress.City);
    }

    [Fact]
    public void Register_WithNameAndTaxId_StartsActive()
    {
        var fornecedor = Supplier.Register("Angoferragens, Lda.", "5417123456", Morada());

        Assert.Equal(SupplierStatus.Active, fornecedor.Status);
        Assert.NotEqual(Guid.Empty, fornecedor.Id);
        Assert.Null(fornecedor.Iban);
    }

    [Fact]
    public void Register_NormalizesTaxId()
    {
        // O NIF vem da factura, e na factura vem com espaços.
        var fornecedor = Supplier.Register("Angoferragens", " 5417 123 456 ", Morada());

        Assert.Equal("5417123456", fornecedor.TaxId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Supplier.Register(name, "5417123456", Morada()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutTaxId_Throws(string taxId)
    {
        // Sem NIF não há como identificar o fornecedor na factura de compra.
        Assert.Throws<ArgumentException>(() => Supplier.Register("Angoferragens", taxId, Morada()));
    }

    [Fact]
    public void SetIban_WithValidIban_Normalizes()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        // Os IBAN vêm quase sempre agrupados de quatro em quatro.
        fornecedor.SetIban("AO71 0006 0000 0109 1312 3415 1");

        Assert.Equal(IbanValido, fornecedor.Iban);
    }

    [Fact]
    public void SetIban_WithWrongCheckDigits_Throws()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        // Último dígito trocado — exactamente o erro de quem copia à mão, e a
        // razão de o mod-97 existir. Um IBAN errado paga a outra pessoa, e esse
        // dinheiro não volta por se corrigir o registo.
        Assert.Throws<ArgumentException>(() => fornecedor.SetIban("AO71000600000109131234152"));
    }

    [Fact]
    public void SetIban_WithRejectedIban_KeepsThePreviousOne()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());
        fornecedor.SetIban(IbanValido);

        Assert.Throws<ArgumentException>(() => fornecedor.SetIban("AO71000600000109131234152"));

        // A recusa não pode deixar o fornecedor sem conta: seria trocar um IBAN
        // bom por nenhum, por causa de uma tentativa falhada.
        Assert.Equal(IbanValido, fornecedor.Iban);
    }

    [Theory]
    [InlineData("AO7100060000010913123415")]      // curto de mais para este país
    [InlineData("A071000600000109131234151")]     // dígito onde devia estar letra
    [InlineData("AOX1000600000109131234151")]     // letra onde devia estar dígito
    [InlineData("AO71-0006-0000@0109131234151")]  // caracter não alfanumérico
    [InlineData("AO71")]                          // abaixo do mínimo da norma
    public void SetIban_WithMalformedIban_Throws(string iban)
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        Assert.Throws<ArgumentException>(() => fornecedor.SetIban(iban));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetIban_WithNothing_ClearsIt(string? iban)
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());
        fornecedor.SetIban(IbanValido);

        fornecedor.SetIban(iban);

        // Apagar é legítimo: um fornecedor pode deixar de ter conta registada.
        Assert.Null(fornecedor.Iban);
    }

    [Theory]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("PT50000201231234567890154")]
    public void SetIban_AcceptsIbansFromOtherCountries(string iban)
    {
        // A verificação é da norma, não de Angola — e o comprimento por país
        // não é verificado de propósito. Um fornecedor estrangeiro tem de
        // caber, e o IBAN do Reino Unido tem 22 caracteres contra os 25 de cá.
        var fornecedor = Supplier.Register("Fornecedor estrangeiro", "5417123456", Morada());

        fornecedor.SetIban(iban);

        Assert.Equal(iban, fornecedor.Iban);
    }

    [Fact]
    public void Deactivate_ThenReactivate_RoundTrips()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.Deactivate();
        Assert.Equal(SupplierStatus.Inactive, fornecedor.Status);

        fornecedor.Reactivate();
        Assert.Equal(SupplierStatus.Active, fornecedor.Status);
    }

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.Deactivate();
        fornecedor.Deactivate();

        Assert.Equal(SupplierStatus.Inactive, fornecedor.Status);
    }

    [Fact]
    public void ChangeContacts_WithBlanks_StoresNull()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.ChangeContacts("  ", "  ");

        // Cadeia em branco não é contacto — guardá-la faria uma lista de
        // fornecedores "com email" que não tem email nenhum.
        Assert.Null(fornecedor.Email);
        Assert.Null(fornecedor.Phone);
    }

    [Fact]
    public void CorrectTaxId_NormalizesToo()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.CorrectTaxId(" 5999 888 777 ");

        Assert.Equal("5999888777", fornecedor.TaxId);
    }

    [Fact]
    public void Version_IsNeverTouchedByTheDomain()
    {
        var fornecedor = Supplier.Register("Angoferragens", "5417123456", Morada());

        fornecedor.Rename("Angoferragens SU, Lda.");
        fornecedor.Deactivate();
        fornecedor.SetIban(IbanValido);

        // Quem o incrementa é o SaveChangesAsync do DbContext, para todas as
        // entidades alteradas de uma vez (ADR-025). Se o domínio começasse a
        // mexer-lhe, contaria duas vezes.
        Assert.Equal(0, fornecedor.Version);
    }
}
