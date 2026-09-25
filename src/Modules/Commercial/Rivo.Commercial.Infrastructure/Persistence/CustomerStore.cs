using Microsoft.EntityFrameworkCore;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Commercial.Infrastructure.Persistence;

public sealed class CustomerStore(CommercialDbContext context) : ICustomerStore
{
    public async Task<Customer?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

    public async Task<Customer?> FindForUpdateAsync(Guid customerId, CancellationToken cancellationToken) =>
        await context.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

    public async Task<Customer?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken) =>
        await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TaxId == taxId, cancellationToken);

    public async Task<Customer?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public async Task AddAccountLinkAsync(CustomerAccountLink link, CancellationToken cancellationToken) =>
        await context.CustomerAccountLinks.AddAsync(link, cancellationToken);

    public async Task<CustomerAccountLink?> FindOpenAccountLinkAsync(
        Guid customerId,
        CancellationToken cancellationToken) =>
        await context.CustomerAccountLinks
            .FirstOrDefaultAsync(
                l => l.CustomerId == customerId && l.UnlinkedOn == null,
                cancellationToken);

    public async Task<IReadOnlyList<CustomerAccountLink>> ListAccountLinksAsync(
        Guid customerId,
        CancellationToken cancellationToken) =>
        await context.CustomerAccountLinks
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.LinkedOn)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Customer> Items, int? TotalCount)> ListAsync(
        bool includeInactive,
        PageRequest? pagina,
        CancellationToken cancellationToken)
    {
        var query = context.Customers.AsNoTracking().AsQueryable();

        // Por omissão só os activos: quem lista para facturar não quer ver
        // clientes desactivados no meio.
        if (!includeInactive)
        {
            query = query.Where(c => c.Status == CustomerStatus.Active);
        }

        query = query.OrderBy(c => c.Name);

        int? total = null;

        if (pagina is { } p)
        {
            total = await query.CountAsync(cancellationToken);
            query = query.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);
        }

        var itens = await query.ToListAsync(cancellationToken);
        return (itens, total);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken) =>
        await context.Customers.AddAsync(customer, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
