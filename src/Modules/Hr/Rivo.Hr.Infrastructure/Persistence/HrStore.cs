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

    public async Task<PositionAssignment?> FindAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken) =>
        await context.PositionAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListAssignmentsAwaitingDecisionAsync(
        int batchSize,
        CancellationToken cancellationToken) =>
        // Só os identificadores: quem reconcilia carrega cada uma rastreada,
        // uma a uma, para que uma falha não arraste o lote inteiro.
        await context.PositionAssignments
            .AsNoTracking()
            .Where(a => a.Status == PositionAssignmentStatus.Pending && a.ApprovalRequestId != null)
            // Mais antigas primeiro: uma atribuição à espera há mais tempo é a
            // que mais provavelmente já tem decisão.
            .OrderBy(a => a.EffectiveFrom)
            .Take(batchSize)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

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

    public async Task<IReadOnlyList<EmploymentContract>> ListContractsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        await context.EmploymentContracts
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.StartsOn)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmploymentContract>> ListContractsAsync(CancellationToken cancellationToken) =>
        await context.EmploymentContracts
            .AsNoTracking()
            .OrderByDescending(c => c.StartsOn)
            .ToListAsync(cancellationToken);

    public async Task<EmploymentContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken) =>
        await context.EmploymentContracts.FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);

    public async Task AddContractAsync(EmploymentContract contract, CancellationToken cancellationToken) =>
        await context.EmploymentContracts.AddAsync(contract, cancellationToken);

    public async Task<AttendanceRecord?> FindAttendanceAsync(
        Guid employeeId,
        DateOnly day,
        CancellationToken cancellationToken) =>
        // Rastreada, e não AsNoTracking: quem a procura vai marcar a saída ou
        // justificar a falta, e precisa que o EF Core veja a alteração.
        await context.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Day == day, cancellationToken);

    public async Task<IReadOnlyList<AttendanceRecord>> ListAttendanceAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var query = context.AttendanceRecords
            .AsNoTracking()
            .Where(a => a.Day >= from && a.Day <= to);

        if (employeeId is not null)
        {
            query = query.Where(a => a.EmployeeId == employeeId);
        }

        return await query
            .OrderByDescending(a => a.Day)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAttendanceAsync(AttendanceRecord record, CancellationToken cancellationToken) =>
        await context.AttendanceRecords.AddAsync(record, cancellationToken);

    public async Task<IReadOnlyList<Benefit>> ListBenefitsAsync(CancellationToken cancellationToken) =>
        await context.Benefits.AsNoTracking().OrderBy(b => b.Name).ToListAsync(cancellationToken);

    public async Task<Benefit?> FindBenefitAsync(Guid benefitId, CancellationToken cancellationToken) =>
        await context.Benefits.FirstOrDefaultAsync(b => b.Id == benefitId, cancellationToken);

    public async Task AddBenefitAsync(Benefit benefit, CancellationToken cancellationToken) =>
        await context.Benefits.AddAsync(benefit, cancellationToken);

    public async Task<IReadOnlyList<BenefitEnrolment>> ListEnrolmentsAsync(
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var query = context.BenefitEnrolments.AsNoTracking();

        if (employeeId is not null)
        {
            query = query.Where(e => e.EmployeeId == employeeId);
        }

        return await query.OrderByDescending(e => e.StartsOn).ToListAsync(cancellationToken);
    }

    public async Task<BenefitEnrolment?> FindEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken) =>
        await context.BenefitEnrolments.FirstOrDefaultAsync(e => e.Id == enrolmentId, cancellationToken);

    public async Task AddEnrolmentAsync(BenefitEnrolment enrolment, CancellationToken cancellationToken) =>
        await context.BenefitEnrolments.AddAsync(enrolment, cancellationToken);

    public async Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken cancellationToken) =>
        await context.JobOpenings.AsNoTracking().OrderBy(o => o.Title).ToListAsync(cancellationToken);

    public async Task<JobOpening?> FindJobOpeningAsync(Guid openingId, CancellationToken cancellationToken) =>
        await context.JobOpenings.FirstOrDefaultAsync(o => o.Id == openingId, cancellationToken);

    public async Task AddJobOpeningAsync(JobOpening opening, CancellationToken cancellationToken) =>
        await context.JobOpenings.AddAsync(opening, cancellationToken);

    public async Task<IReadOnlyList<Candidate>> ListCandidatesAsync(
        Guid? openingId,
        CancellationToken cancellationToken)
    {
        var query = context.Candidates.AsNoTracking();

        if (openingId is not null)
        {
            query = query.Where(c => c.JobOpeningId == openingId);
        }

        return await query.OrderBy(c => c.Stage).ThenBy(c => c.AppliedOn).ToListAsync(cancellationToken);
    }

    public async Task<Candidate?> FindCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.Candidates.FirstOrDefaultAsync(c => c.Id == candidateId, cancellationToken);

    public async Task AddCandidateAsync(Candidate candidate, CancellationToken cancellationToken) =>
        await context.Candidates.AddAsync(candidate, cancellationToken);

    public async Task<IReadOnlyList<EmployeeLifecycleProcess>> ListLifecycleProcessesAsync(
        LifecycleKind? kind,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        // `Include` das tarefas: quem lista quer ver o progresso, e sem elas a
        // contagem de pendentes daria sempre zero.
        var query = context.LifecycleProcesses.AsNoTracking().Include(p => p.Tasks).AsQueryable();

        if (kind is not null)
        {
            query = query.Where(p => p.Kind == kind);
        }

        if (employeeId is not null)
        {
            query = query.Where(p => p.EmployeeId == employeeId);
        }

        return await query.OrderByDescending(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<EmployeeLifecycleProcess?> FindLifecycleProcessAsync(
        Guid processId,
        CancellationToken cancellationToken) =>
        // Rastreado e com as tarefas: quem o procura vai concluir uma tarefa ou
        // fechar o processo, e a regra que impede fechar com pendentes precisa
        // de as ver.
        await context.LifecycleProcesses
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == processId, cancellationToken);

    public async Task AddLifecycleProcessAsync(
        EmployeeLifecycleProcess process,
        CancellationToken cancellationToken) =>
        await context.LifecycleProcesses.AddAsync(process, cancellationToken);

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
