using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Base para dobras de <see cref="IHrStore"/> que só precisam de alguns
/// membros.
///
/// <para>
/// <c>IHrStore</c> é uma interface para o módulo inteiro — 44 membros, por
/// decisão explícita de não a partir por agregado. Um caso de uso usa três ou
/// quatro. Sem esta base, cada dobra escrita à mão (ADR-022) traria quarenta
/// implementações vazias a esconder as que interessam.
/// </para>
///
/// <para>
/// Tudo lança por omissão, e isso é a parte útil: se um caso de uso passar a
/// tocar num membro que o teste não previu, o teste falha a dizer qual, em vez
/// de receber <c>null</c> e seguir por um caminho que ninguém quis exercitar.
/// </para>
/// </summary>
internal abstract class HrStoreParcial : IHrStore
{
    private static Task<T> NaoUsado<T>([System.Runtime.CompilerServices.CallerMemberName] string membro = "") =>
        throw new NotSupportedException($"O teste não previu uma chamada a {membro}.");

    private static Task NaoUsado([System.Runtime.CompilerServices.CallerMemberName] string membro = "") =>
        throw new NotSupportedException($"O teste não previu uma chamada a {membro}.");

    public virtual Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<Employee?>();
    public virtual Task<Employee?> FindEmployeeByUserIdAsync(Guid userId, CancellationToken cancellationToken) => NaoUsado<Employee?>();
    public virtual Task<(IReadOnlyList<Employee> Items, int? TotalCount)> ListEmployeesAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<Employee>, int?)>();
    public virtual Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task AddAccountLinkAsync(EmployeeAccountLink link, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<EmployeeAccountLink?> FindOpenAccountLinkAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<EmployeeAccountLink?>();
    public virtual Task<IReadOnlyList<EmployeeAccountLink>> ListAccountLinksAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<EmployeeAccountLink>>();
    public virtual Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken) => NaoUsado<bool>();
    public virtual Task<Department?> FindDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) => NaoUsado<Department?>();
    public virtual Task<(IReadOnlyList<Department> Items, int? TotalCount)> ListDepartmentsAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<Department>, int?)>();
    public virtual Task AddDepartmentAsync(Department department, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken) => NaoUsado<Position?>();
    public virtual Task<(IReadOnlyList<Position> Items, int? TotalCount)> ListPositionsAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<Position>, int?)>();
    public virtual Task AddPositionAsync(Position position, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task AddAssignmentAsync(PositionAssignment assignment, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<PositionAssignment?> FindAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken) => NaoUsado<PositionAssignment?>();
    public virtual Task<IReadOnlyList<Guid>> ListAssignmentsAwaitingDecisionAsync(int batchSize, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<Guid>>();
    public virtual Task<(IReadOnlyList<LeaveRequest> Items, int? TotalCount)> ListLeaveAsync(Guid? employeeId, PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<LeaveRequest>, int?)>();
    public virtual Task<LeaveRequest?> FindLeaveAsync(Guid leaveId, CancellationToken cancellationToken) => NaoUsado<LeaveRequest?>();
    public virtual Task AddLeaveAsync(LeaveRequest leave, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<IReadOnlyList<Guid>> ListLeaveAwaitingDecisionAsync(int batchSize, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<Guid>>();
    public virtual Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<PositionAssignment>>();
    public virtual Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForPositionAsync(Guid positionId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<PositionAssignment>>();
    public virtual Task<IReadOnlyList<EmploymentContract>> ListContractsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<EmploymentContract>>();
    public virtual Task<(IReadOnlyList<EmploymentContract> Items, int? TotalCount)> ListContractsAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<EmploymentContract>, int?)>();
    public virtual Task<EmploymentContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken) => NaoUsado<EmploymentContract?>();
    public virtual Task AddContractAsync(EmploymentContract contract, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<AttendanceRecord?> FindAttendanceAsync(Guid employeeId, DateOnly day, CancellationToken cancellationToken) => NaoUsado<AttendanceRecord?>();
    public virtual Task<(IReadOnlyList<AttendanceRecord> Items, int? TotalCount)> ListAttendanceAsync(DateOnly from, DateOnly to, Guid? employeeId, bool anomaliesOnly, PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<AttendanceRecord>, int?)>();
    public virtual Task AddAttendanceAsync(AttendanceRecord record, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<(IReadOnlyList<Benefit> Items, int? TotalCount)> ListBenefitsAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<Benefit>, int?)>();
    public virtual Task<Benefit?> FindBenefitAsync(Guid benefitId, CancellationToken cancellationToken) => NaoUsado<Benefit?>();
    public virtual Task AddBenefitAsync(Benefit benefit, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<(IReadOnlyList<BenefitEnrolment> Items, int? TotalCount)> ListEnrolmentsAsync(Guid? employeeId, PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<BenefitEnrolment>, int?)>();
    public virtual Task<BenefitEnrolment?> FindEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken) => NaoUsado<BenefitEnrolment?>();
    public virtual Task AddEnrolmentAsync(BenefitEnrolment enrolment, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<(IReadOnlyList<JobOpening> Items, int? TotalCount)> ListJobOpeningsAsync(PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<JobOpening>, int?)>();
    public virtual Task<JobOpening?> FindJobOpeningAsync(Guid openingId, CancellationToken cancellationToken) => NaoUsado<JobOpening?>();
    public virtual Task AddJobOpeningAsync(JobOpening opening, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<(IReadOnlyList<Candidate> Items, int? TotalCount)> ListCandidatesAsync(Guid? openingId, PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<Candidate>, int?)>();
    public virtual Task<Candidate?> FindCandidateAsync(Guid candidateId, CancellationToken cancellationToken) => NaoUsado<Candidate?>();
    public virtual Task AddCandidateAsync(Candidate candidate, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<(IReadOnlyList<EmployeeLifecycleProcess> Items, int? TotalCount)> ListLifecycleProcessesAsync(LifecycleKind? kind, Guid? employeeId, PageRequest? pagina, CancellationToken cancellationToken) => NaoUsado<(IReadOnlyList<EmployeeLifecycleProcess>, int?)>();
    public virtual Task<EmployeeLifecycleProcess?> FindLifecycleProcessAsync(Guid processId, CancellationToken cancellationToken) => NaoUsado<EmployeeLifecycleProcess?>();
    public virtual Task AddLifecycleProcessAsync(EmployeeLifecycleProcess process, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task AddEmployeeDocumentAsync(EmployeeDocument link, CancellationToken cancellationToken) => NaoUsado();
    public virtual Task<IReadOnlyList<EmployeeDocument>> ListEmployeeDocumentsAsync(Guid employeeId, CancellationToken cancellationToken) => NaoUsado<IReadOnlyList<EmployeeDocument>>();
    public virtual Task SaveChangesAsync(CancellationToken cancellationToken) => NaoUsado();
}
