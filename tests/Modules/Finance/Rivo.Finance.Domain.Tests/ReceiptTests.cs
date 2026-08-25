using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Uma factura diz o que é devido; um recibo diz o que foi pago. Confundi-los é
/// o que faz um mapa de dívida mentir.
/// </summary>
public class ReceiptTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 25);

    private static DocumentNumber NumeroRg() =>
        DocumentSeries.Open(DocumentType.RG, "S001").Allocate();

    private static InvoicedParty Cliente() =>
        new("Kianda Lda", "5417000000", "Rua Rainha Ginga 12", "Luanda", "AO");

    private static Receipt Recibo(params NewSettlement[] liquidacoes) =>
        Receipt.Register(
            NumeroRg(), Hoje, Guid.CreateVersion7(), Cliente(), "AOA", PaymentMethod.MB,
            liquidacoes.Length > 0
                ? liquidacoes
                : [new NewSettlement(Guid.CreateVersion7(), "FT S001/1", 114_000m)]);

    [Fact]
    public void ReciboRegistado_NumeraSeEmSerieRg()
    {
        Assert.Equal("RG S001/1", Recibo().Number.Formatted);
    }

    [Fact]
    public void ReciboEmSerieDeFactura_ERecusado()
    {
        var numeroFt = DocumentSeries.Open(DocumentType.FT, "S001").Allocate();

        Assert.Throws<ArgumentException>(() =>
            Receipt.Register(numeroFt, Hoje, null, Cliente(), "AOA", PaymentMethod.NU,
                [new NewSettlement(Guid.CreateVersion7(), "FT S001/1", 100m)]));
    }

    /// <summary>
    /// O caso corrente de quem paga um extracto de uma vez. Sem saber que
    /// quantia foi para que factura, não há como saber o que ficou por receber.
    /// </summary>
    [Fact]
    public void UmReciboLiquidaVariasFacturas()
    {
        var recibo = Recibo(
            new NewSettlement(Guid.CreateVersion7(), "FT S001/1", 100_000m),
            new NewSettlement(Guid.CreateVersion7(), "FT S001/2", 50_000m));

        Assert.Equal(2, recibo.Lines.Count);
        Assert.Equal(150_000m, recibo.Total);
        Assert.Equal([1, 2], recibo.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void ReciboGuardaAReferenciaTextualDaFactura()
    {
        var recibo = Recibo();

        Assert.Equal("FT S001/1", recibo.Lines[0].InvoiceNumber);
        Assert.NotEqual(Guid.Empty, recibo.Lines[0].SalesInvoiceId);
    }

    [Fact]
    public void MeioDePagamentoEDoSaft()
    {
        Assert.Equal(PaymentMethod.MB, Recibo().Method);
    }

    // ---- recusas ----

    [Fact]
    public void ReciboSemLiquidacoes_ERecusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Receipt.Register(NumeroRg(), Hoje, null, Cliente(), "AOA", PaymentMethod.NU, []));
    }

    /// <summary>
    /// A mesma factura duas vezes esconderia metade do valor de quem some as
    /// linhas por factura.
    /// </summary>
    [Fact]
    public void MesmaFacturaDuasVezesNoMesmoRecibo_ERecusada()
    {
        var factura = Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() =>
            Recibo(
                new NewSettlement(factura, "FT S001/1", 100m),
                new NewSettlement(factura, "FT S001/1", 50m)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void QuantiaNaoPositiva_ERecusada(decimal quantia)
    {
        // Devolver dinheiro e nota de credito, nao recibo ao contrario.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Recibo(new NewSettlement(Guid.CreateVersion7(), "FT S001/1", quantia)));
    }

    [Fact]
    public void LiquidacaoSemFactura_ERecusada()
    {
        Assert.Throws<ArgumentException>(() =>
            Recibo(new NewSettlement(Guid.Empty, "FT S001/1", 100m)));
    }

    [Theory]
    [InlineData("Kwanza")]
    [InlineData("AO")]
    public void MoedaQueNaoEIso4217_ERecusada(string moeda)
    {
        Assert.Throws<ArgumentException>(() =>
            Receipt.Register(NumeroRg(), Hoje, null, Cliente(), moeda, PaymentMethod.NU,
                [new NewSettlement(Guid.CreateVersion7(), "FT S001/1", 100m)]));
    }

    // ---- estorno ----

    /// <summary>
    /// É o que acontece quando um cheque volta: a quantia deixa de contar e a
    /// dívida volta a existir.
    /// </summary>
    [Fact]
    public void EstornarMantemLinhasETotal()
    {
        var recibo = Recibo();
        recibo.Cancel("Cheque devolvido", DateTimeOffset.UtcNow);

        Assert.Equal(InvoiceStatus.Cancelled, recibo.Status);
        Assert.Equal("Cheque devolvido", recibo.CancellationReason);
        Assert.Single(recibo.Lines);
        Assert.Equal(114_000m, recibo.Total);
    }

    [Fact]
    public void EstornarSemMotivo_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => Recibo().Cancel("  ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EstornarDuasVezes_ERecusado()
    {
        var recibo = Recibo();
        recibo.Cancel("Cheque devolvido", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            recibo.Cancel("Outra vez", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var recibo = Recibo();
        recibo.Cancel("Estorno", DateTimeOffset.UtcNow);

        Assert.Equal(0, recibo.Version);
    }
}
