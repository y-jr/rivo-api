using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

public class GetMyStatementTests
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
    public async Task ExecuteAsync_UserLinkedToCustomer_ReturnsStatement()
    {
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(customerId));
        var linha = new CustomerStatementLine(Hoje, "Factura", "FT S001/1", "Debit", 114_000m, 114_000m);
        var receivables = new FakeReceivablesOverview()
            .WithStatement(customerId, new CustomerStatementView(0m, [linha], 114_000m));

        var useCase = new GetMyStatement(directory, receivables);

        var result = await useCase.ExecuteAsync(userId, Inicio, Hoje, "AOA", CancellationToken.None);

        Assert.Equal(MyStatementOutcome.Found, result.Outcome);
        Assert.Equal(114_000m, result.Statement!.ClosingBalance);
        Assert.Single(result.Statement.Lines);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutCustomerLink_ReturnsNotLinked()
    {
        var useCase = new GetMyStatement(new FakeCustomerDirectory(), new FakeReceivablesOverview());

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), Inicio, Hoje, "AOA", CancellationToken.None);

        Assert.Equal(MyStatementOutcome.NotLinked, result.Outcome);
        Assert.Null(result.Statement);
    }

    [Fact]
    public async Task ExecuteAsync_InvertedWindow_IsRejected()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeCustomerDirectory().WithCustomer(userId, Cliente(Guid.NewGuid()));
        var useCase = new GetMyStatement(directory, new FakeReceivablesOverview());

        var result = await useCase.ExecuteAsync(userId, Hoje, Inicio, "AOA", CancellationToken.None);

        Assert.Equal(MyStatementOutcome.Rejected, result.Outcome);
    }
}
