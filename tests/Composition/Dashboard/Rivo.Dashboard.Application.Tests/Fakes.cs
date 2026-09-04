using Rivo.Finance.Contracts;

namespace Rivo.Dashboard.Application.Tests;

/// <summary>Duplos escritos à mão, sem biblioteca de mocks — ADR-022.</summary>
internal sealed class FakeReceivablesOverview : IReceivablesOverview
{
    public decimal NetRevenue { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public IReadOnlyList<CustomerRevenueView> TopCustomers { get; set; } = [];

    /// <summary>O último pedido recebido, para os testes que verificam o que se propagou.</summary>
    public (DateOnly From, DateOnly To, string Currency)? LastRevenueRequest { get; private set; }

    public Task<decimal> GetNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        LastRevenueRequest = (from, to, currency);
        return Task.FromResult(NetRevenue);
    }

    public Task<decimal> GetOutstandingReceivablesAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult(OutstandingReceivables);

    public Task<IReadOnlyList<CustomerRevenueView>> GetTopCustomersAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken) =>
        Task.FromResult(TopCustomers);

    public Task<decimal> GetCustomerNetRevenueAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<decimal> GetCustomerOutstandingAsync(
        Guid customerId, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<IReadOnlyList<CustomerInvoiceView>> ListCustomerInvoicesAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerInvoiceView>>([]);

    public Task<CustomerStatementView> GetCustomerStatementAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(new CustomerStatementView(0m, [], 0m));

    public Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MonthlyAmount>>([]);
}

internal sealed class FakePayablesOverview : IPayablesOverview
{
    public decimal NetExpenses { get; set; }
    public decimal OutstandingPayables { get; set; }

    public Task<decimal> GetNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(NetExpenses);

    public Task<decimal> GetOutstandingPayablesAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult(OutstandingPayables);

    public Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MonthlyAmount>>([]);
}
