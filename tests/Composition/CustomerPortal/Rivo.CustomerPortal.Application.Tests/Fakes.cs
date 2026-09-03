using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application.Tests;

internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    private readonly Dictionary<Guid, CustomerReference> _byUserId = [];

    public FakeCustomerDirectory WithCustomer(Guid userId, CustomerReference customer)
    {
        _byUserId[userId] = customer;
        return this;
    }

    public Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.Values.FirstOrDefault(c => c.CustomerId == customerId));

    public Task<CustomerReference?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_byUserId.GetValueOrDefault(userId));
}

internal sealed class FakeReceivablesOverview : IReceivablesOverview
{
    private readonly Dictionary<Guid, decimal> _netRevenue = [];
    private readonly Dictionary<Guid, decimal> _outstanding = [];
    private readonly Dictionary<Guid, List<CustomerInvoiceView>> _invoices = [];
    private readonly Dictionary<Guid, CustomerStatementView> _statements = [];

    /// <summary>Regista o que <see cref="GetCustomerNetRevenueAsync"/> devolve para este cliente.</summary>
    public FakeReceivablesOverview WithNetRevenue(Guid customerId, decimal value)
    {
        _netRevenue[customerId] = value;
        return this;
    }

    public FakeReceivablesOverview WithOutstanding(Guid customerId, decimal value)
    {
        _outstanding[customerId] = value;
        return this;
    }

    public FakeReceivablesOverview WithInvoice(Guid customerId, CustomerInvoiceView invoice)
    {
        if (!_invoices.TryGetValue(customerId, out var lista))
        {
            lista = [];
            _invoices[customerId] = lista;
        }

        lista.Add(invoice);
        return this;
    }

    public Task<decimal> GetNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_netRevenue.Values.Sum());

    public Task<decimal> GetOutstandingReceivablesAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_outstanding.Values.Sum());

    public Task<IReadOnlyList<CustomerRevenueView>> GetTopCustomersAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerRevenueView>>([]);

    public Task<decimal> GetCustomerNetRevenueAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_netRevenue.GetValueOrDefault(customerId));

    public Task<decimal> GetCustomerOutstandingAsync(
        Guid customerId, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_outstanding.GetValueOrDefault(customerId));

    public Task<IReadOnlyList<CustomerInvoiceView>> ListCustomerInvoicesAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerInvoiceView>>(
            _invoices.GetValueOrDefault(customerId, []));

    public FakeReceivablesOverview WithStatement(Guid customerId, CustomerStatementView statement)
    {
        _statements[customerId] = statement;
        return this;
    }

    public Task<CustomerStatementView> GetCustomerStatementAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_statements.GetValueOrDefault(customerId, new CustomerStatementView(0m, [], 0m)));
}
