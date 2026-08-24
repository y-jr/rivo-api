using Microsoft.EntityFrameworkCore;
using Rivo.Commercial.Application.Abstractions;
using Rivo.Commercial.Domain;

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

    public async Task<IReadOnlyList<Customer>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = context.Customers.AsNoTracking().AsQueryable();

        // Por omissão só os activos: quem lista para facturar não quer ver
        // clientes desactivados no meio.
        if (!includeInactive)
        {
            query = query.Where(c => c.Status == CustomerStatus.Active);
        }

        return await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken) =>
        await context.Customers.AddAsync(customer, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
