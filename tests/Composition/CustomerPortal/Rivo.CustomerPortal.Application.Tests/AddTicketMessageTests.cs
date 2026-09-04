using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class AddTicketMessageTests
{
    private static CustomerReference Cliente(Guid customerId) => new(
        customerId,
        "Kianda Lda",
        "5417000000",
        CustomerStatus.Active,
        new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    [Fact]
    public async Task ExecuteAsync_UserLinkedToCustomer_ResolveOClienteEDelegaAMessaging()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var conversationId = Guid.CreateVersion7();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var messaging = new FakeCustomerMessaging()
            .WillReturn(Messaging.Contracts.SendMessageResult.Sent(conversationId, Guid.CreateVersion7()));

        var useCase = new AddTicketMessage(directory, messaging);

        var result = await useCase.ExecuteAsync(userId, conversationId, "Continua sem funcionar.", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Sent, result.Outcome);
        Assert.Equal(customerId, messaging.LastCustomerId);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new AddTicketMessage(new FakeCustomerDirectory(), new FakeCustomerMessaging());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Guid.CreateVersion7(), "Corpo", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.NotLinked, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_TicketNaoEncontrado_TraduzODesfecho()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(Guid.NewGuid()));
        var messaging = new FakeCustomerMessaging().WillReturn(Messaging.Contracts.SendMessageResult.NotFound());

        var useCase = new AddTicketMessage(directory, messaging);

        var result = await useCase.ExecuteAsync(userId, Guid.CreateVersion7(), "Corpo", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.NotFound, result.Outcome);
    }
}
