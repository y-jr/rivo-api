using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class OpenTicketTests
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
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var conversationId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var messaging = new FakeCustomerMessaging()
            .WillReturn(Messaging.Contracts.SendMessageResult.Sent(conversationId, messageId));

        var useCase = new OpenTicket(directory, messaging);

        var result = await useCase.ExecuteAsync(userId, "Problema com login", "Não consigo entrar.", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Sent, result.Outcome);
        Assert.Equal(conversationId, result.ConversationId);
        Assert.Equal(customerId, messaging.LastCustomerId);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new OpenTicket(new FakeCustomerDirectory(), new FakeCustomerMessaging());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), "Assunto", "Corpo", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.NotLinked, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_MessagingRecusa_TraduzODesfecho()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(Guid.NewGuid()));
        var messaging = new FakeCustomerMessaging()
            .WillReturn(Messaging.Contracts.SendMessageResult.Rejected("Um ticket precisa de assunto."));

        var useCase = new OpenTicket(directory, messaging);

        var result = await useCase.ExecuteAsync(userId, "  ", "Corpo", CancellationToken.None);

        Assert.Equal(SendMessageOutcome.Rejected, result.Outcome);
        Assert.Equal("Um ticket precisa de assunto.", result.Error);
    }
}
