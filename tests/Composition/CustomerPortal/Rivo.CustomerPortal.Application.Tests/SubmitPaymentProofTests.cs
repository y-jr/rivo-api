using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class SubmitPaymentProofTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 3);

    private static CustomerReference Cliente(Guid customerId) => new(
        customerId,
        "Kianda Lda",
        "5417000000",
        CustomerStatus.Active,
        new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    [Fact]
    public async Task ExecuteAsync_UserLinkedToCustomer_ResolveOClienteEDelegaAFinance()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var claimId = Guid.CreateVersion7();
        var payments = new FakeCustomerPayments().WillReturn(SubmitPaymentClaimResult.Submitted(claimId));

        var useCase = new SubmitPaymentProof(directory, payments);

        var result = await useCase.ExecuteAsync(
            userId, Guid.CreateVersion7(), 100_000m, Hoje, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentProofOutcome.Submitted, result.Outcome);
        Assert.Equal(claimId, result.ClaimId);
        Assert.Equal(customerId, payments.LastCustomerId);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new SubmitPaymentProof(new FakeCustomerDirectory(), new FakeCustomerPayments());

        var result = await useCase.ExecuteAsync(
            Guid.NewGuid(), Guid.CreateVersion7(), 100_000m, Hoje, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentProofOutcome.NotLinked, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_FinanceRecusa_TraduzODesfechoSemAdivinhar()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(Guid.NewGuid()));
        var payments = new FakeCustomerPayments()
            .WillReturn(SubmitPaymentClaimResult.ExceedsOutstanding("A factura tem 50.000 em aberto."));

        var useCase = new SubmitPaymentProof(directory, payments);

        var result = await useCase.ExecuteAsync(
            userId, Guid.CreateVersion7(), 999_999m, Hoje, Guid.CreateVersion7(), null, CancellationToken.None);

        Assert.Equal(SubmitPaymentProofOutcome.ExceedsOutstanding, result.Outcome);
        Assert.Equal("A factura tem 50.000 em aberto.", result.Error);
    }
}
