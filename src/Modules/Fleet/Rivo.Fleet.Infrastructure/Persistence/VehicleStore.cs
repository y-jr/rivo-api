using Microsoft.EntityFrameworkCore;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Infrastructure.Persistence;

public sealed class VehicleStore(FleetDbContext context) : IVehicleStore
{
    public async Task<Vehicle?> FindAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        await context.Vehicles.AsNoTracking()
            .Include(v => v.Maintenances)
            .Include(v => v.Assignments)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

    public async Task<Vehicle?> FindForUpdateAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        await context.Vehicles
            .Include(v => v.Maintenances)
            .Include(v => v.Assignments)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

    public async Task<Vehicle?> FindByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken) =>
        await context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.PlateNumber == plateNumber, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = context.Vehicles.AsNoTracking()
            .Include(v => v.Maintenances)
            .Include(v => v.Assignments)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(v => v.Status != VehicleStatus.Inactive);
        }

        return await query.OrderBy(v => v.PlateNumber).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken) =>
        await context.Vehicles.AddAsync(vehicle, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
