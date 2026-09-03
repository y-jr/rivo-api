using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class ListMyPaymentClaimsTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 3);

    private static CustomerReference Cliente(Guid customerId) => new(
        customerId,
        "Kianda Lda",
        "5417000000",
        CustomerStatus.Active,
        new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    [Fact]
    public async Task ExecuteAsync_UserLinkedToCustomer_ReturnsOwnClaims()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var claim = new PaymentClaimView(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 100_000m, Hoje, "Pending", null, DateTimeOffset.UtcNow);
        var payments = new FakeCustomerPayments().WithClaim(customerId, claim);

        var useCase = new ListMyPaymentClaims(directory, payments);

        var result = await useCase.ExecuteAsync(userId, CancellationToken.None);

        Assert.Equal(ListMyPaymentClaimsOutcome.Found, result.Outcome);
        Assert.Single(result.Claims!);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new ListMyPaymentClaims(new FakeCustomerDirectory(), new FakeCustomerPayments());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ListMyPaymentClaimsOutcome.NotLinked, result.Outcome);
        Assert.Null(result.Claims);
    }
}
