using Rivo.Commercial.Contracts;
using Rivo.Messaging.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class ListMyTicketsTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    private static CustomerReference Cliente(Guid customerId) => new(
        customerId,
        "Kianda Lda",
        "5417000000",
        CustomerStatus.Active,
        new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    [Fact]
    public async Task ExecuteAsync_UserLinkedToCustomer_ReturnsOwnTickets()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var ticket = new ConversationView(
            Guid.CreateVersion7(), "Ticket", "Problema com login", "Open", Agora, null,
            [new MessageView(Guid.CreateVersion7(), "Customer", userId, "Não consigo entrar.", Agora)]);
        var messaging = new FakeCustomerMessaging().WithTicket(customerId, ticket);

        var useCase = new ListMyTickets(directory, messaging);

        var result = await useCase.ExecuteAsync(userId, CancellationToken.None);

        Assert.Equal(ListMyMessagesOutcome.Found, result.Outcome);
        Assert.Single(result.Conversations!);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new ListMyTickets(new FakeCustomerDirectory(), new FakeCustomerMessaging());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ListMyMessagesOutcome.NotLinked, result.Outcome);
        Assert.Null(result.Conversations);
    }
}
