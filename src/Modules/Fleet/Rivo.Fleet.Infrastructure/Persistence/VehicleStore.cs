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
            .Include(v => v.Plans)
            .Include(v => v.Trips)
            .Include(v => v.Expenses)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

    public async Task<Vehicle?> FindForUpdateAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        await context.Vehicles
            .Include(v => v.Maintenances)
            .Include(v => v.Assignments)
            .Include(v => v.Plans)
            .Include(v => v.Trips)
            .Include(v => v.Expenses)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);

    public async Task<Vehicle?> FindByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken) =>
        await context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.PlateNumber == plateNumber, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = context.Vehicles.AsNoTracking()
            .Include(v => v.Maintenances)
            .Include(v => v.Assignments)
            .Include(v => v.Plans)
            .Include(v => v.Trips)
            .Include(v => v.Expenses)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(v => v.Status != VehicleStatus.Inactive);
        }

        return await query.OrderBy(v => v.PlateNumber).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Vehicle>> ListWithDuePlansAsync(
        DateOnly asOf, int withinDays, CancellationToken cancellationToken)
    {
        var limite = asOf.AddDays(withinDays);

        return await context.Vehicles.AsNoTracking()
            .Include(v => v.Plans)
            .Where(v => v.Plans.Any(p => p.IsActive && p.NextDueOn <= limite))
            .OrderBy(v => v.PlateNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken) =>
        await context.Vehicles.AddAsync(vehicle, cancellationToken);

    public async Task<decimal> SumExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await context.Set<FleetExpense>()
            .AsNoTracking()
            .Where(e => e.OccurredOn >= from && e.OccurredOn <= to)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

    public async Task<decimal> SumTripDistanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await context.Set<VehicleTrip>()
            .AsNoTracking()
            .Where(t => t.StartedOn >= from && t.StartedOn <= to)
            .SumAsync(t => (decimal?)(t.EndOdometer - t.StartOdometer), cancellationToken) ?? 0m;

    public async Task<decimal> SumMaintenanceCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await context.Set<MaintenanceRecord>()
            .AsNoTracking()
            .Where(m => m.EndedOn != null && m.EndedOn >= from && m.EndedOn <= to && m.Cost != null)
            .SumAsync(m => (decimal?)m.Cost, cancellationToken) ?? 0m;

    public async Task AddVehicleDocumentAsync(VehicleDocument link, CancellationToken cancellationToken) =>
        await context.VehicleDocuments.AddAsync(link, cancellationToken);

    public async Task<IReadOnlyList<VehicleDocument>> ListVehicleDocumentsAsync(
        Guid vehicleId, CancellationToken cancellationToken) =>
        await context.VehicleDocuments.AsNoTracking()
            .Where(l => l.VehicleId == vehicleId)
            .OrderByDescending(l => l.AttachedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
