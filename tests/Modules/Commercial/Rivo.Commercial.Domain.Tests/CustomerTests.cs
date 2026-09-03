using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Domain.Tests;

/// <summary>
/// O Cliente existe para poder ser facturado. As invariantes são as que o
/// SAF-T AO exige do elemento <c>Customer</c> — ver ADR-036.
/// </summary>
public class CustomerTests
{
    private static BillingAddress Morada() => new("Rua Rainha Ginga 12", "Luanda", "AO");

    private static Customer Registado() =>
        Customer.Register("Kianda Lda", "5417000000", Morada());

    [Fact]
    public void ClienteRegistado_NasceActivo()
    {
        Assert.Equal(CustomerStatus.Active, Registado().Status);
    }

    [Fact]
    public void SemNome_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Customer.Register("  ", "5417000000", Morada()));
    }

    /// <summary>
    /// Sem NIF não há como identificar o cliente no documento fiscal, e o campo
    /// não se preenche depois de a factura estar emitida.
    /// </summary>
    [Fact]
    public void SemNif_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Customer.Register("Kianda Lda", "", Morada()));
    }

    [Fact]
    public void SemMorada_ERecusado()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Customer.Register("Kianda Lda", "5417000000", null!));
    }

    [Theory]
    [InlineData(" 5417 000 000 ", "5417000000")]
    [InlineData("ao5417000000", "AO5417000000")]
    public void NifENormalizado(string introduzido, string esperado)
    {
        var cliente = Customer.Register("Kianda Lda", introduzido, Morada());

        Assert.Equal(esperado, cliente.TaxId);
    }

    /// <summary>
    /// O formato do NIF angolano não está verificado em fonte primária, e
    /// `CLAUDE.md` proíbe implementar regras fiscais a partir de levantamento
    /// provisório. Um validador inventado recusaria clientes legítimos.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    public void NifDeFormatoInvulgar_EAceite(string nif)
    {
        var cliente = Customer.Register("Kianda Lda", nif, Morada());

        Assert.False(string.IsNullOrWhiteSpace(cliente.TaxId));
    }

    [Fact]
    public void MoradaIncompleta_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => new BillingAddress("", "Luanda", "AO"));
        Assert.Throws<ArgumentException>(() => new BillingAddress("Rua X", "", "AO"));
    }

    [Theory]
    [InlineData("Angola")]
    [InlineData("A")]
    [InlineData("")]
    public void PaisQueNaoEAlpha2_ERecusado(string pais)
    {
        Assert.Throws<ArgumentException>(() => new BillingAddress("Rua X", "Luanda", pais));
    }

    [Fact]
    public void PaisEGuardadoEmMaiusculas()
    {
        Assert.Equal("AO", new BillingAddress("Rua X", "Luanda", "ao").Country);
    }

    [Fact]
    public void ContactosSaoOpcionais()
    {
        var cliente = Registado();

        Assert.Null(cliente.Email);
        Assert.Null(cliente.Phone);
    }

    [Fact]
    public void ContactoEmBranco_GuardaSeComoAusente()
    {
        var cliente = Registado();
        cliente.ChangeContacts("   ", "+244 900 000 000");

        Assert.Null(cliente.Email);
        Assert.Equal("+244 900 000 000", cliente.Phone);
    }

    [Fact]
    public void ClienteDesactivado_PodeSerReactivado()
    {
        var cliente = Registado();

        cliente.Deactivate();
        Assert.Equal(CustomerStatus.Inactive, cliente.Status);

        cliente.Reactivate();
        Assert.Equal(CustomerStatus.Active, cliente.Status);
    }

    /// <summary>Repetir não é erro: quem chama duas vezes obtém o mesmo estado.</summary>
    [Fact]
    public void DesactivarDuasVezes_EIdempotente()
    {
        var cliente = Registado();

        cliente.Deactivate();
        cliente.Deactivate();

        Assert.Equal(CustomerStatus.Inactive, cliente.Status);
    }

    /// <summary>
    /// O contador é do `SaveChangesAsync` do DbContext, não do domínio. Se isto
    /// falhar, alguém repôs `Version++` e o contador sobe duas vezes por escrita.
    /// </summary>
    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var cliente = Registado();

        cliente.Rename("Kianda, S.A.");
        cliente.ChangeBillingAddress(new BillingAddress("Rua Nova 3", "Benguela", "AO"));

        Assert.Equal(0, cliente.Version);
    }

    [Fact]
    public void CorrigirNif_Normaliza()
    {
        var cliente = Registado();
        cliente.CorrectTaxId(" 5999 111 222 ");

        Assert.Equal("5999111222", cliente.TaxId);
    }

    [Fact]
    public void RenomearParaVazio_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => Registado().Rename(" "));
    }

    [Fact]
    public void LinkToUser_LigaUmaContaDepois()
    {
        var cliente = Registado();
        var userId = Guid.CreateVersion7();

        cliente.LinkToUser(userId);

        Assert.Equal(userId, cliente.UserId);
    }

    [Fact]
    public void AssignOwner_AtribuiOVendedorResponsavel()
    {
        var cliente = Registado();
        var employeeId = Guid.CreateVersion7();

        cliente.AssignOwner(employeeId);

        Assert.Equal(employeeId, cliente.AssignedToEmployeeId);
    }

    [Fact]
    public void AssignOwner_ComNulo_RemoveAAtribuicao()
    {
        var cliente = Registado();
        cliente.AssignOwner(Guid.CreateVersion7());

        cliente.AssignOwner(null);

        Assert.Null(cliente.AssignedToEmployeeId);
    }
}
