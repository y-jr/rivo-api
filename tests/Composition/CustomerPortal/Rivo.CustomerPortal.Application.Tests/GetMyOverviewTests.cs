using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class GetMyOverviewTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 1);
    private static readonly DateOnly Inicio = new(2026, 1, 1);

    private static CustomerReference Cliente(Guid customerId) => new(
        customerId,
        "Kianda Lda",
        "5417000000",
        CustomerStatus.Active,
        new BillingAddress("Rua Rainha Ginga 12", "Luanda", "AO"));

    [Fact]
    public async Task ExecuteAsync_UserLinkedToCustomer_ReturnsFound()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var receivables = new FakeReceivablesOverview()
            .WithNetRevenue(customerId, 250_000m)
            .WithOutstanding(customerId, 114_000m)
            .WithInvoice(customerId, new CustomerInvoiceView(
                Guid.NewGuid(), "FT S001/1", Hoje, "Normal", "AOA", 114_000m));

        var useCase = new GetMyOverview(directory, receivables);

        var result = await useCase.ExecuteAsync(userId, Inicio, Hoje, "AOA", CancellationToken.None);

        Assert.Equal(MyOverviewOutcome.Found, result.Outcome);
        Assert.Equal(customerId, result.Overview!.CustomerId);
        Assert.Equal("Kianda Lda", result.Overview.CustomerName);
        Assert.Equal(250_000m, result.Overview.NetRevenue);
        Assert.Equal(114_000m, result.Overview.Outstanding);
        Assert.Single(result.Overview.Invoices);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new GetMyOverview(new FakeCustomerDirectory(), new FakeReceivablesOverview());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Inicio, Hoje, "AOA", CancellationToken.None);

        Assert.Equal(MyOverviewOutcome.NotLinked, result.Outcome);
        Assert.Null(result.Overview);
    }

    [Fact]
    public async Task ExecuteAsync_InvertedWindow_IsRejected()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(Guid.NewGuid()));
        var useCase = new GetMyOverview(directory, new FakeReceivablesOverview());

        var result = await useCase.ExecuteAsync(userId, Hoje, Inicio, "AOA", CancellationToken.None);

        Assert.Equal(MyOverviewOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_NeverReturnsAnotherUsersCustomer()
    {
        // Mesma estrutura de EmployeePortal: sem parametro nenhum para pedir
        // "o cliente de outra pessoa" -- o teste fixa o comportamento.
        var outroUserId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(outroUserId, Cliente(Guid.NewGuid()));
        var useCase = new GetMyOverview(directory, new FakeReceivablesOverview());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Inicio, Hoje, "AOA", CancellationToken.None);

        Assert.Equal(MyOverviewOutcome.NotLinked, result.Outcome);
    }
}
