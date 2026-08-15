using Microsoft.EntityFrameworkCore;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Infrastructure.Persistence;

public sealed class HrStore(HrDbContext context) : IHrStore
{
    public async Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

    public async Task<IReadOnlyList<Employee>> ListEmployeesAsync(CancellationToken cancellationToken) =>
        await context.Employees.AsNoTracking().OrderBy(e => e.FullName).ToListAsync(cancellationToken);

    public async Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken) =>
        await context.Employees.AddAsync(employee, cancellationToken);

    public async Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken) =>
        await context.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken);

    public async Task<IReadOnlyList<Department>> ListDepartmentsAsync(CancellationToken cancellationToken) =>
        await context.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);

    public async Task AddDepartmentAsync(Department department, CancellationToken cancellationToken) =>
        await context.Departments.AddAsync(department, cancellationToken);

    public async Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken) =>
        await context.Positions.FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);

    public async Task<IReadOnlyList<Position>> ListPositionsAsync(CancellationToken cancellationToken) =>
        await context.Positions.AsNoTracking().OrderBy(p => p.HierarchyLevel).ThenBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task AddPositionAsync(Position position, CancellationToken cancellationToken) =>
        await context.Positions.AddAsync(position, cancellationToken);

    public async Task AddAssignmentAsync(PositionAssignment assignment, CancellationToken cancellationToken) =>
        await context.PositionAssignments.AddAsync(assignment, cancellationToken);

    public async Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        await context.PositionAssignments
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            // Mais recente primeiro: a resolução do cargo à data toma a
            // primeira atribuição efectiva que encontrar.
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForPositionAsync(
        Guid positionId,
        CancellationToken cancellationToken) =>
        await context.PositionAssignments
            .AsNoTracking()
            .Where(a => a.PositionId == positionId)
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public async Task AddEmployeeDocumentAsync(EmployeeDocument link, CancellationToken cancellationToken) =>
        await context.EmployeeDocuments.AddAsync(link, cancellationToken);

    public async Task<IReadOnlyList<EmployeeDocument>> ListEmployeeDocumentsAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        await context.EmployeeDocuments
            .AsNoTracking()
            .Where(link => link.EmployeeId == employeeId)
            .OrderByDescending(link => link.AttachedAt)
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
