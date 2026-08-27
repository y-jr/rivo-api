using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Domain.Tests;

public class PurchaseOrderTests
{
    private static readonly Guid Requisicao = Guid.CreateVersion7();
    private static readonly Guid Fornecedor = Guid.CreateVersion7();
    private static readonly DateOnly Hoje = new(2026, 8, 27);
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static PurchaseOrder Ordem() =>
        PurchaseOrder.Issue(Requisicao, Fornecedor, "AOA", Hoje, null);

    [Fact]
    public void Issue_StartsIssued()
    {
        var ordem = Ordem();

        Assert.Equal(PurchaseOrderStatus.Issued, ordem.Status);
        Assert.Equal(Requisicao, ordem.RequisitionId);
        Assert.Equal(Fornecedor, ordem.SupplierId);
        Assert.Equal(0m, ordem.Total);
        Assert.Null(ordem.CancelledAt);
    }

    [Fact]
    public void Issue_WithoutRequisition_Throws()
    {
        // "Ordem de Compra so e gerada apos decisao Aprovado registada em
        // `approval`" — sem requisicao nao ha decisao a que a ordem se agarre,
        // e encomendar sem decisao e o que a governanca existe para impedir.
        Assert.Throws<ArgumentException>(
            () => PurchaseOrder.Issue(Guid.Empty, Fornecedor, "AOA", Hoje, null));
    }

    [Fact]
    public void Issue_WithoutSupplier_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PurchaseOrder.Issue(Requisicao, Guid.Empty, "AOA", Hoje, null));
    }

    [Theory]
    [InlineData("KZ")]
    [InlineData("KWANZA")]
    [InlineData("")]
    public void Issue_WithMalformedCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(
            () => PurchaseOrder.Issue(Requisicao, Fornecedor, currency, Hoje, null));
    }

    [Fact]
    public void Issue_NormalizesCurrency()
    {
        var ordem = PurchaseOrder.Issue(Requisicao, Fornecedor, "aoa", Hoje, null);

        Assert.Equal("AOA", ordem.Currency);
    }

    [Fact]
    public void Issue_WithDeliveryBeforeIssue_Throws()
    {
        // Uma entrega anterior à emissão não é uma data optimista, é um engano
        // de digitação — e ficaria a contar como atraso desde o primeiro dia.
        Assert.Throws<ArgumentException>(
            () => PurchaseOrder.Issue(Requisicao, Fornecedor, "AOA", Hoje, Hoje.AddDays(-1)));
    }

    [Fact]
    public void Issue_WithDeliveryOnTheSameDay_IsAllowed()
    {
        var ordem = PurchaseOrder.Issue(Requisicao, Fornecedor, "AOA", Hoje, Hoje);

        Assert.Equal(Hoje, ordem.ExpectedOn);
    }

    [Fact]
    public void Total_SumsTheLines()
    {
        var ordem = Ordem();

        ordem.AddLine("Portatil 14 pol", 2, 860000m);
        ordem.AddLine("Rato sem fios", 2, 11000m);

        Assert.Equal(1_742_000m, ordem.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLine_WithNonPositiveQuantity_Throws(decimal quantity)
    {
        var ordem = Ordem();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ordem.AddLine("Portatil", quantity, 860000m));
    }

    [Fact]
    public void AddLine_WithNegativePrice_Throws()
    {
        var ordem = Ordem();

        Assert.Throws<ArgumentOutOfRangeException>(() => ordem.AddLine("Portatil", 1, -1m));
    }

    [Fact]
    public void AddLine_WithZeroPrice_IsAllowed()
    {
        // Uma linha a zero é uma oferta ou um brinde do fornecedor, e faz parte
        // da encomenda tanto como as outras — a recepção vai contá-la.
        var ordem = Ordem();

        var linha = ordem.AddLine("Cabo de rede, oferta", 1, 0m);

        Assert.Equal(0m, linha.LineTotal);
        Assert.Single(ordem.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddLine_WithoutDescription_Throws(string description)
    {
        var ordem = Ordem();

        Assert.Throws<ArgumentException>(() => ordem.AddLine(description, 1, 100m));
    }

    [Fact]
    public void AddLine_AfterCancellation_Throws()
    {
        // Uma ordem cancelada já foi retirada ao fornecedor. Acrescentar-lhe uma
        // linha ressuscitaria uma encomenda que ninguém espera.
        var ordem = Ordem();
        ordem.AddLine("Portatil", 1, 860000m);
        ordem.Cancel("Fornecedor sem stock.", Agora);

        Assert.Throws<InvalidOperationException>(() => ordem.AddLine("Mais um", 1, 860000m));
    }

    [Fact]
    public void Cancel_KeepsTheReasonAndTheMoment()
    {
        var ordem = Ordem();
        ordem.AddLine("Portatil", 1, 860000m);

        ordem.Cancel("Fornecedor sem stock.", Agora);

        Assert.Equal(PurchaseOrderStatus.Cancelled, ordem.Status);
        Assert.Equal("Fornecedor sem stock.", ordem.CancellationReason);
        Assert.Equal(Agora, ordem.CancelledAt);

        // BR-14: as linhas ficam. A ordem existiu, e o fornecedor pode ter
        // agido sobre ela.
        Assert.Single(ordem.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutReason_Throws(string reason)
    {
        var ordem = Ordem();

        Assert.Throws<ArgumentException>(() => ordem.Cancel(reason, Agora));
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var ordem = Ordem();
        ordem.Cancel("Fornecedor sem stock.", Agora);

        Assert.Throws<InvalidOperationException>(() => ordem.Cancel("Outra vez.", Agora));
    }

    [Fact]
    public void Version_IsNeverTouchedByTheDomain()
    {
        var ordem = Ordem();
        ordem.AddLine("Portatil", 1, 860000m);
        ordem.Cancel("Fornecedor sem stock.", Agora);

        Assert.Equal(0, ordem.Version);
    }
}
