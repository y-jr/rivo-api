using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Messaging.Application.Abstractions;
using Rivo.Messaging.Domain;
using Rivo.Notifications.Contracts;

namespace Rivo.Messaging.Application.Tests;

/// <summary>Duplos escritos à mão, sem biblioteca de mocks — ADR-022.</summary>
internal sealed class FakeConversationStore : IConversationStore
{
    private readonly Dictionary<Guid, Conversation> _conversations = [];

    public int SaveCount { get; private set; }

    public FakeConversationStore With(Conversation conversation)
    {
        _conversations[conversation.Id] = conversation;
        return this;
    }

    public Task<Conversation?> FindOpenByCustomerAsync(
        Guid customerId, ConversationKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(_conversations.Values
            .FirstOrDefault(c => c.CustomerId == customerId && c.Kind == kind && c.Status == ConversationStatus.Open));

    public Task<Conversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(_conversations.GetValueOrDefault(conversationId));

    public Task<Conversation?> FindForUpdateAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(_conversations.GetValueOrDefault(conversationId));

    public Task<IReadOnlyList<Conversation>> ListByCustomerAsync(
        Guid customerId, ConversationKind? kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Conversation>>(
            [.. _conversations.Values.Where(c => c.CustomerId == customerId && (kind is null || c.Kind == kind))]);

    public Task<IReadOnlyList<Conversation>> ListAsync(
        ConversationStatus? status, ConversationKind? kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Conversation>>(
            [.. _conversations.Values.Where(c =>
                (status is null || c.Status == status) && (kind is null || c.Kind == kind))]);

    public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        _conversations[conversation.Id] = conversation;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    private readonly Dictionary<Guid, CustomerReference> _byId = [];

    public FakeCustomerDirectory With(CustomerReference customer)
    {
        _byId[customer.CustomerId] = customer;
        return this;
    }

    public Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(customerId));

    public Task<CustomerReference?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado por messaging — a composição já resolve o cliente.");
}

internal sealed class FakeEmployeeDirectory : IEmployeeDirectory
{
    private readonly Dictionary<Guid, EmployeeReference> _byId = [];

    public FakeEmployeeDirectory With(EmployeeReference employee)
    {
        _byId[employee.EmployeeId] = employee;
        return this;
    }

    public Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(employeeId));

    public Task<EmployeeReference?> FindByUserIdAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado por messaging.");

    public Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(
        Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Não usado por messaging.");
}

internal sealed class FakeNotifier : INotifier
{
    public List<NotificationRequest> Queued { get; } = [];

    public Task QueueAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        Queued.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Records { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
