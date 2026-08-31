using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.Abstractions;

/// <summary>
/// Persistência do módulo. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
///
/// Uma interface para o módulo inteiro, e não uma por agregado: os agregados
/// de `hr` são consultados em conjunto (um colaborador com o seu cargo e
/// departamento) e separá-los agora seria cerimónia sem benefício.
/// </summary>
public interface IHrStore
{
    // --- Colaboradores ---

    Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// O colaborador ligado a uma conta de `identity`, se existir — é o
    /// caminho por onde o Portal do Colaborador resolve "o próprio"
    /// (ADR-042). Nunca mais do que um: <c>UserId</c> é único quando
    /// preenchido (índice único em <c>HrDbContext</c>, segunda linha de
    /// defesa da verificação em <c>HireEmployee</c>).
    /// </summary>
    Task<Employee?> FindEmployeeByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Employee>> ListEmployeesAsync(CancellationToken cancellationToken);

    Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken);

    // --- Departamentos ---

    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Department>> ListDepartmentsAsync(CancellationToken cancellationToken);

    Task AddDepartmentAsync(Department department, CancellationToken cancellationToken);

    // --- Cargos ---

    Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Position>> ListPositionsAsync(CancellationToken cancellationToken);

    Task AddPositionAsync(Position position, CancellationToken cancellationToken);

    // --- Atribuições de cargo ---

    Task AddAssignmentAsync(PositionAssignment assignment, CancellationToken cancellationToken);

    /// <summary>Atribuições de um colaborador, para resolver o cargo à data.</summary>
    /// <summary>
    /// Uma atribuição concreta, rastreada — quem a procura vai promovê-la a
    /// efectiva ou fechá-la depois da decisão de `approval`.
    /// </summary>
    Task<PositionAssignment?> FindAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Atribuições pendentes que já têm processo de aprovação — as que o worker
    /// de reconciliação vai perguntar a `approval` se já foram decididas.
    ///
    /// <para>
    /// Uma pendente <strong>sem</strong> processo fica de fora de propósito:
    /// não há a quem perguntar, e promovê-la seria conferir autoridade sem
    /// ninguém a ter aprovado (BR-20).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAssignmentsAwaitingDecisionAsync(
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Pedidos de férias, de um colaborador ou de toda a empresa.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListLeaveAsync(
        Guid? employeeId,
        CancellationToken cancellationToken);

    Task<LeaveRequest?> FindLeaveAsync(Guid leaveId, CancellationToken cancellationToken);

    Task AddLeaveAsync(LeaveRequest leave, CancellationToken cancellationToken);

    /// <summary>Pedidos de férias pendentes com processo associado — a fila do worker.</summary>
    Task<IReadOnlyList<Guid>> ListLeaveAwaitingDecisionAsync(
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Atribuições de um cargo, para resolver quem o ocupa à data.</summary>
    Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForPositionAsync(Guid positionId, CancellationToken cancellationToken);

    // --- Documentos anexados (ADR-009: a ligação vive aqui, não em `documents`) ---

    /// <summary>
    /// Contratos de um colaborador, do mais recente para o mais antigo.
    ///
    /// <para>
    /// Devolve o histórico completo, e não só o que está em vigor: é sobre ele
    /// que se verifica a sobreposição de vigências antes de celebrar um novo.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EmploymentContract>> ListContractsForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EmploymentContract>> ListContractsAsync(CancellationToken cancellationToken);

    Task<EmploymentContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken);

    Task AddContractAsync(EmploymentContract contract, CancellationToken cancellationToken);

    /// <summary>
    /// A marcação de um colaborador num dia, se existir. É o que impede abrir
    /// o mesmo dia duas vezes.
    /// </summary>
    Task<AttendanceRecord?> FindAttendanceAsync(
        Guid employeeId,
        DateOnly day,
        CancellationToken cancellationToken);

    /// <summary>Marcações num intervalo de dias, para toda a empresa.</summary>
    Task<IReadOnlyList<AttendanceRecord>> ListAttendanceAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId,
        CancellationToken cancellationToken);

    Task AddAttendanceAsync(AttendanceRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<Benefit>> ListBenefitsAsync(CancellationToken cancellationToken);

    Task<Benefit?> FindBenefitAsync(Guid benefitId, CancellationToken cancellationToken);

    Task AddBenefitAsync(Benefit benefit, CancellationToken cancellationToken);

    Task<IReadOnlyList<BenefitEnrolment>> ListEnrolmentsAsync(
        Guid? employeeId,
        CancellationToken cancellationToken);

    Task<BenefitEnrolment?> FindEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken);

    Task AddEnrolmentAsync(BenefitEnrolment enrolment, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken cancellationToken);

    Task<JobOpening?> FindJobOpeningAsync(Guid openingId, CancellationToken cancellationToken);

    Task AddJobOpeningAsync(JobOpening opening, CancellationToken cancellationToken);

    Task<IReadOnlyList<Candidate>> ListCandidatesAsync(Guid? openingId, CancellationToken cancellationToken);

    Task<Candidate?> FindCandidateAsync(Guid candidateId, CancellationToken cancellationToken);

    Task AddCandidateAsync(Candidate candidate, CancellationToken cancellationToken);

    /// <summary>
    /// Processos de entrada e saída. <strong>Traz as tarefas.</strong> A regra
    /// que impede concluir com tarefas pendentes precisa de as ver — carregar
    /// o processo sem elas faria a verificação passar sempre.
    /// </summary>
    Task<IReadOnlyList<EmployeeLifecycleProcess>> ListLifecycleProcessesAsync(
        LifecycleKind? kind,
        Guid? employeeId,
        CancellationToken cancellationToken);

    Task<EmployeeLifecycleProcess?> FindLifecycleProcessAsync(
        Guid processId,
        CancellationToken cancellationToken);

    Task AddLifecycleProcessAsync(EmployeeLifecycleProcess process, CancellationToken cancellationToken);

    Task AddEmployeeDocumentAsync(EmployeeDocument link, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeDocument>> ListEmployeeDocumentsAsync(Guid employeeId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
