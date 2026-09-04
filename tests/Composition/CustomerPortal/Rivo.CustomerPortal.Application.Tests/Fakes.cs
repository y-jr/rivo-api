using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;
using Rivo.Messaging.Contracts;

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

    public Task<CustomerRegistrationResult> RegisterAsync(
        string name, string taxId, string addressDetail, string city, string country,
        string? email, string? phone, Guid actorId, CancellationToken cancellationToken) =>
        Task.FromResult(CustomerRegistrationResult.Success(Guid.CreateVersion7()));
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

    public Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MonthlyAmount>>([]);
}

internal sealed class FakeCustomerPayments : ICustomerPayments
{
    private readonly Dictionary<Guid, List<PaymentClaimView>> _claims = [];

    private SubmitPaymentClaimResult _nextResult = SubmitPaymentClaimResult.Submitted(Guid.CreateVersion7());

    /// <summary>O que <see cref="SubmitClaimAsync"/> devolve na próxima chamada — o teste decide o desfecho de `finance`.</summary>
    public FakeCustomerPayments WillReturn(SubmitPaymentClaimResult result)
    {
        _nextResult = result;
        return this;
    }

    /// <summary>Regista o último pedido recebido, para o teste confirmar que o `customerId` resolvido chegou.</summary>
    public Guid? LastCustomerId { get; private set; }

    public FakeCustomerPayments WithClaim(Guid customerId, PaymentClaimView claim)
    {
        if (!_claims.TryGetValue(customerId, out var lista))
        {
            lista = [];
            _claims[customerId] = lista;
        }

        lista.Add(claim);
        return this;
    }

    public Task<SubmitPaymentClaimResult> SubmitClaimAsync(
        Guid customerId, Guid salesInvoiceId, decimal amount, DateOnly paidOn, Guid documentId,
        Guid submittedByUserId, string? notes, CancellationToken cancellationToken)
    {
        LastCustomerId = customerId;
        return Task.FromResult(_nextResult);
    }

    public Task<IReadOnlyList<PaymentClaimView>> ListMyClaimsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PaymentClaimView>>(_claims.GetValueOrDefault(customerId, []));
}

internal sealed class FakeCustomerMessaging : ICustomerMessaging
{
    private readonly Dictionary<Guid, List<ConversationView>> _conversations = [];
    private readonly Dictionary<Guid, List<ConversationView>> _tickets = [];

    // Qualificado por inteiro: `Rivo.CustomerPortal.Application.SendMessageResult`
    // (o tipo do caso de uso da composição) tem o mesmo nome e ganharia à
    // procura por `using` — é o tipo do contrato de `messaging` que esta
    // classe implementa.
    private Messaging.Contracts.SendMessageResult _nextResult =
        Messaging.Contracts.SendMessageResult.Sent(Guid.CreateVersion7(), Guid.CreateVersion7());

    public Guid? LastCustomerId { get; private set; }

    public FakeCustomerMessaging WillReturn(Messaging.Contracts.SendMessageResult result)
    {
        _nextResult = result;
        return this;
    }

    public FakeCustomerMessaging WithConversation(Guid customerId, ConversationView conversation)
    {
        if (!_conversations.TryGetValue(customerId, out var lista))
        {
            lista = [];
            _conversations[customerId] = lista;
        }

        lista.Add(conversation);
        return this;
    }

    public Task<Messaging.Contracts.SendMessageResult> SendMessageAsync(
        Guid customerId, Guid senderUserId, string body, CancellationToken cancellationToken)
    {
        LastCustomerId = customerId;
        return Task.FromResult(_nextResult);
    }

    public Task<IReadOnlyList<ConversationView>> ListMyConversationsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationView>>(_conversations.GetValueOrDefault(customerId, []));

    public FakeCustomerMessaging WithTicket(Guid customerId, ConversationView ticket)
    {
        if (!_tickets.TryGetValue(customerId, out var lista))
        {
            lista = [];
            _tickets[customerId] = lista;
        }

        lista.Add(ticket);
        return this;
    }

    public Task<Messaging.Contracts.SendMessageResult> OpenTicketAsync(
        Guid customerId, Guid senderUserId, string subject, string body, CancellationToken cancellationToken)
    {
        LastCustomerId = customerId;
        return Task.FromResult(_nextResult);
    }

    public Task<Messaging.Contracts.SendMessageResult> AddTicketMessageAsync(
        Guid customerId, Guid conversationId, Guid senderUserId, string body, CancellationToken cancellationToken)
    {
        LastCustomerId = customerId;
        return Task.FromResult(_nextResult);
    }

    public Task<IReadOnlyList<ConversationView>> ListMyTicketsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationView>>(_tickets.GetValueOrDefault(customerId, []));
}
