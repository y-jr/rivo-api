namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// Pedido de confirmação de pagamento (ADR-044) — não é o recibo, é só o
/// pedido até alguém decidir.
/// </summary>
public class PaymentClaimTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 3);

    private static readonly DateTimeOffset Agora = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    private static PaymentClaim Pedido(decimal amount = 100_000m) =>
        PaymentClaim.Submit(
            Guid.CreateVersion7(), Guid.CreateVersion7(), amount, Hoje,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Agora);

    [Fact]
    public void Submit_NasceComoPending()
    {
        Assert.Equal(PaymentClaimStatus.Pending, Pedido().Status);
    }

    [Fact]
    public void Submit_ValorZeroOuNegativo_ERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pedido(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pedido(-1m));
    }

    [Fact]
    public void Submit_SemComprovativo_ERecusado()
    {
        Assert.Throws<ArgumentException>(() => PaymentClaim.Submit(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 100_000m, Hoje,
            Guid.Empty, Guid.CreateVersion7(), null, Agora));
    }

    [Fact]
    public void Confirm_PedidoPendente_FicaConfirmadoComOReciboLigado()
    {
        var pedido = Pedido();
        var reciboId = Guid.CreateVersion7();
        var revisor = Guid.CreateVersion7();

        pedido.Confirm(reciboId, revisor, Agora);

        Assert.Equal(PaymentClaimStatus.Confirmed, pedido.Status);
        Assert.Equal(reciboId, pedido.ReceiptId);
        Assert.Equal(revisor, pedido.ReviewedByUserId);
    }

    [Fact]
    public void Confirm_PedidoJaDecidido_ERecusado()
    {
        var pedido = Pedido();
        pedido.Confirm(Guid.CreateVersion7(), Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(
            () => pedido.Confirm(Guid.CreateVersion7(), Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void Reject_PedidoPendente_FicaRejeitadoComMotivo()
    {
        var pedido = Pedido();
        var revisor = Guid.CreateVersion7();

        pedido.Reject("Comprovativo ilegível.", revisor, Agora);

        Assert.Equal(PaymentClaimStatus.Rejected, pedido.Status);
        Assert.Equal("Comprovativo ilegível.", pedido.RejectionReason);
        Assert.Equal(revisor, pedido.ReviewedByUserId);
    }

    [Fact]
    public void Reject_SemMotivo_ERecusado()
    {
        Assert.Throws<ArgumentException>(
            () => Pedido().Reject("  ", Guid.CreateVersion7(), Agora));
    }

    [Fact]
    public void Reject_PedidoJaConfirmado_ERecusado()
    {
        var pedido = Pedido();
        pedido.Confirm(Guid.CreateVersion7(), Guid.CreateVersion7(), Agora);

        Assert.Throws<InvalidOperationException>(
            () => pedido.Reject("Motivo", Guid.CreateVersion7(), Agora));
    }
}
