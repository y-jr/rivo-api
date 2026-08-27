using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Domain.Tests;

public class GoodsReceiptTests
{
    private static readonly Guid Ordem = Guid.CreateVersion7();
    private static readonly Guid LinhaDaOrdem = Guid.CreateVersion7();
    private static readonly Guid Recebedor = Guid.CreateVersion7();
    private static readonly DateOnly Hoje = new(2026, 8, 27);
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static GoodsReceipt Recepcao() =>
        GoodsReceipt.Register(Ordem, Hoje, Recebedor, "GR 4471");

    [Fact]
    public void Register_StartsRegistered()
    {
        var recepcao = Recepcao();

        Assert.Equal(GoodsReceiptStatus.Registered, recepcao.Status);
        Assert.Equal(Ordem, recepcao.PurchaseOrderId);
        Assert.Equal(Recebedor, recepcao.ReceivedByEmployeeId);
        Assert.Equal("GR 4471", recepcao.DeliveryNote);
        Assert.Empty(recepcao.Lines);
    }

    [Fact]
    public void Register_WithoutOrder_Throws()
    {
        // Nao se recebe o que nao se encomendou: sem ordem nao ha contra que
        // comparar, e o 3-way match perde o lado do meio.
        Assert.Throws<ArgumentException>(
            () => GoodsReceipt.Register(Guid.Empty, Hoje, Recebedor, null));
    }

    [Fact]
    public void Register_WithoutReceiver_Throws()
    {
        // Uma divergencia entre o encomendado e o recebido e uma conversa com
        // alguem, e sem nome nao ha com quem a ter.
        Assert.Throws<ArgumentException>(
            () => GoodsReceipt.Register(Ordem, Hoje, Guid.Empty, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutDeliveryNote_StoresNull(string? deliveryNote)
    {
        // A guia e do fornecedor e nem sempre vem. Guardar cadeia em branco
        // faria uma lista de "recepcoes com guia" que nao tem guia nenhuma.
        var recepcao = GoodsReceipt.Register(Ordem, Hoje, Recebedor, deliveryNote);

        Assert.Null(recepcao.DeliveryNote);
    }

    [Fact]
    public void Register_TrimsTheDeliveryNote()
    {
        var recepcao = GoodsReceipt.Register(Ordem, Hoje, Recebedor, "  GR 4471  ");

        Assert.Equal("GR 4471", recepcao.DeliveryNote);
    }

    [Fact]
    public void AddLine_KeepsTheOrderLineAndTheQuantity()
    {
        var recepcao = Recepcao();

        var linha = recepcao.AddLine(LinhaDaOrdem, 2);

        Assert.Equal(LinhaDaOrdem, linha.PurchaseOrderLineId);
        Assert.Equal(2m, linha.QuantityReceived);
        Assert.Single(recepcao.Lines);
    }

    [Fact]
    public void AddLine_WithFractionalQuantity_IsAllowed()
    {
        // Ha o que se compra a metro e ao quilo. Exigir inteiros excluiria
        // metade do que uma empresa compra.
        var recepcao = Recepcao();

        var linha = recepcao.AddLine(LinhaDaOrdem, 2.5m);

        Assert.Equal(2.5m, linha.QuantityReceived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLine_WithNonPositiveQuantity_Throws(decimal quantity)
    {
        // Nao receber nada nao e uma recepcao: e a ausencia dela, e nao se
        // regista. Uma linha a zero deixaria a ordem com ar de tratada.
        var recepcao = Recepcao();

        Assert.Throws<ArgumentOutOfRangeException>(() => recepcao.AddLine(LinhaDaOrdem, quantity));
    }

    [Fact]
    public void AddLine_WithoutOrderLine_Throws()
    {
        var recepcao = Recepcao();

        Assert.Throws<ArgumentException>(() => recepcao.AddLine(Guid.Empty, 1));
    }

    [Fact]
    public void AddLine_TwiceForTheSameOrderLine_Throws()
    {
        // **Duas contagens da mesma coisa no mesmo acto sao um engano, nao uma
        // entrega parcial.** A parcial e outra recepcao, noutro dia, com outra
        // guia — e e assim que o acumulado se lê.
        var recepcao = Recepcao();
        recepcao.AddLine(LinhaDaOrdem, 1);

        Assert.Throws<InvalidOperationException>(() => recepcao.AddLine(LinhaDaOrdem, 1));
    }

    [Fact]
    public void AddLine_ForAnotherOrderLine_IsAllowed()
    {
        var recepcao = Recepcao();
        recepcao.AddLine(LinhaDaOrdem, 1);

        recepcao.AddLine(Guid.CreateVersion7(), 3);

        Assert.Equal(2, recepcao.Lines.Count);
    }

    [Fact]
    public void AddLine_AfterCancellation_Throws()
    {
        var recepcao = Recepcao();
        recepcao.AddLine(LinhaDaOrdem, 1);
        recepcao.Cancel("Contagem errada.", Agora);

        Assert.Throws<InvalidOperationException>(() => recepcao.AddLine(Guid.CreateVersion7(), 1));
    }

    [Fact]
    public void Cancel_KeepsTheReasonAndTheLines()
    {
        var recepcao = Recepcao();
        recepcao.AddLine(LinhaDaOrdem, 2);

        recepcao.Cancel("Guia lancada na ordem errada.", Agora);

        Assert.Equal(GoodsReceiptStatus.Cancelled, recepcao.Status);
        Assert.Equal("Guia lancada na ordem errada.", recepcao.CancellationReason);
        Assert.Equal(Agora, recepcao.CancelledAt);

        // BR-14: o erro foi cometido, e o registo de o ter sido e a parte que
        // interessa a quem audita.
        Assert.Single(recepcao.Lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutReason_Throws(string reason)
    {
        var recepcao = Recepcao();

        Assert.Throws<ArgumentException>(() => recepcao.Cancel(reason, Agora));
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var recepcao = Recepcao();
        recepcao.Cancel("Contagem errada.", Agora);

        Assert.Throws<InvalidOperationException>(() => recepcao.Cancel("Outra vez.", Agora));
    }

    [Fact]
    public void Version_IsNeverTouchedByTheDomain()
    {
        var recepcao = Recepcao();
        recepcao.AddLine(LinhaDaOrdem, 1);
        recepcao.Cancel("Contagem errada.", Agora);

        Assert.Equal(0, recepcao.Version);
    }
}
