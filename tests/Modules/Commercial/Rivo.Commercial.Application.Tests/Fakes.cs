using Rivo.Audit.Contracts;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Application.Tests;

/// <summary>
/// Clientes e episódios em memória.
///
/// <para>
/// Escrita por inteiro, sem base parcial: <c>ICustomerStore</c> tem dez
/// membros. A base <c>HrStoreParcial</c> existe porque <c>IHrStore</c> tem
/// quarenta e quatro — aqui seria cerimónia sem benefício.
/// </para>
/// </summary>
internal sealed class FakeCustomerStore : ICustomerStore
{
    private readonly List<Customer> _clientes = [];
    private readonly List<CustomerAccountLink> _episodios = [];

    public int Gravacoes { get; private set; }

    public IReadOnlyList<CustomerAccountLink> Episodios => _episodios;

    public Customer Registar(string nome, Guid? userId = null)
    {
        var cliente = Customer.Register(nome, $"NIF{_clientes.Count:D9}",
            new BillingAddress("Rua Principal 1", "Luanda", "AO"));
        _clientes.Add(cliente);

        if (userId is { } conta)
        {
            cliente.LinkToUser(conta);
            _episodios.Add(CustomerAccountLink.Open(
                cliente.Id, conta, DateTimeOffset.UnixEpoch, linkedByUserId: null));
        }

        return cliente;
    }

    public Task<Customer?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_clientes.SingleOrDefault(c => c.Id == customerId));

    public Task<Customer?> FindForUpdateAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_clientes.SingleOrDefault(c => c.Id == customerId));

    public Task<Customer?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken) =>
        Task.FromResult(_clientes.SingleOrDefault(c => c.TaxId == taxId));

    public Task<Customer?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_clientes.SingleOrDefault(c => c.UserId == userId));

    public Task AddAccountLinkAsync(CustomerAccountLink link, CancellationToken cancellationToken)
    {
        _episodios.Add(link);
        return Task.CompletedTask;
    }

    public Task<CustomerAccountLink?> FindOpenAccountLinkAsync(
        Guid customerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_episodios.SingleOrDefault(l => l.CustomerId == customerId && l.IsOpen));

    public Task<IReadOnlyList<CustomerAccountLink>> ListAccountLinksAsync(
        Guid customerId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerAccountLink>>(
            [.. _episodios.Where(l => l.CustomerId == customerId).OrderByDescending(l => l.LinkedOn)]);

    public Task<IReadOnlyList<Customer>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Customer>>([.. _clientes]);

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        _clientes.Add(customer);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>Recolhe o que foi auditado. Escrita à mão (ADR-022).</summary>
internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
