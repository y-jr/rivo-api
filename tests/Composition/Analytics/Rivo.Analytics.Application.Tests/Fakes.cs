using Rivo.Finance.Contracts;
using Rivo.Fleet.Contracts;
using Rivo.Inventory.Contracts;

namespace Rivo.Analytics.Application.Tests;

/// <summary>Duplos escritos à mão, sem biblioteca de mocks — ADR-022.</summary>
internal sealed class FakeReceivablesOverview : IReceivablesOverview
{
    public IReadOnlyList<MonthlyAmount> MonthlyRevenue { get; set; } = [];

    /// <summary>O último pedido recebido, para os testes que verificam o que se propagou.</summary>
    public (DateOnly From, DateOnly To, string Currency)? LastMonthlyRequest { get; private set; }

    public Task<decimal> GetNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<decimal> GetOutstandingReceivablesAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<IReadOnlyList<CustomerRevenueView>> GetTopCustomersAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerRevenueView>>([]);

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
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken)
    {
        LastMonthlyRequest = (from, to, currency);
        return Task.FromResult(MonthlyRevenue);
    }
}

internal sealed class FakePayablesOverview : IPayablesOverview
{
    public IReadOnlyList<MonthlyAmount> MonthlyExpenses { get; set; } = [];

    public Task<decimal> GetNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<decimal> GetOutstandingPayablesAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult(0m);

    public Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(MonthlyExpenses);
}

internal sealed class FakeFleetActivityOverview : IFleetActivityOverview
{
    public decimal PeriodExpenses { get; set; }
    public decimal PeriodDistance { get; set; }
    public decimal PeriodMaintenanceCost { get; set; }

    public Task<decimal> GetPeriodExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PeriodExpenses);

    public Task<decimal> GetPeriodDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PeriodDistance);

    public Task<decimal> GetPeriodMaintenanceCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PeriodMaintenanceCost);
}

internal sealed class FakeInventoryValuationOverview : IInventoryValuationOverview
{
    public decimal CurrentStockValue { get; set; }
    public decimal PeriodValuation { get; set; }

    public Task<decimal> GetCurrentStockValueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentStockValue);

    public Task<decimal> GetPeriodValuationAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(PeriodValuation);
}
