using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Contracts;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

public sealed class ListEmployees(IHrStore store)
{
    public async Task<IReadOnlyList<EmployeeView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var employees = await store.ListEmployeesAsync(cancellationToken);

        return [.. employees.Select(e => new EmployeeView(
            e.Id, e.FullName, e.Status.ToString(), e.DepartmentId, e.UserId, e.HiredOn))];
    }
}

public sealed record EmployeeView(
    Guid EmployeeId,
    string FullName,
    string Status,
    Guid? DepartmentId,
    Guid? UserId,
    DateTimeOffset HiredOn);

public sealed class HireEmployee(IHrStore store, IAuditTrail audit)
{
    public async Task<HireEmployeeResult> ExecuteAsync(
        string fullName,
        Guid? departmentId,
        Guid? userId,
        DateTimeOffset hiredOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Departamento desconhecido é recusado em vez de aceite como nulo:
        // aceitar em silêncio deixaria o colaborador fora do organograma sem
        // ninguém reparar.
        if (departmentId is not null && !await store.DepartmentExistsAsync(departmentId.Value, cancellationToken))
        {
            return HireEmployeeResult.DepartmentNotFound();
        }

        // Uma conta liga-se, no máximo, a um colaborador — é o que o Portal
        // do Colaborador passa a confiar para resolver "o próprio" (ADR-042).
        // Verificado aqui, primeira linha de defesa; o índice único em
        // `HrDbContext` é a segunda.
        if (userId is not null && await store.FindEmployeeByUserIdAsync(userId.Value, cancellationToken) is not null)
        {
            return HireEmployeeResult.UserAlreadyLinked();
        }

        var employee = Employee.Hire(fullName, departmentId, userId, hiredOn);

        await store.AddEmployeeAsync(employee, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeHired,
                HrAuditEntityTypes.Employee,
                employee.Id.ToString(),
                context),
            cancellationToken);

        return HireEmployeeResult.Success(employee.Id);
    }
}

public enum HireEmployeeOutcome
{
    Hired,
    DepartmentNotFound,
    UserAlreadyLinked,
}

public sealed record HireEmployeeResult(HireEmployeeOutcome Outcome, Guid? EmployeeId, string? Error)
{
    public bool Succeeded => Outcome == HireEmployeeOutcome.Hired;

    public static HireEmployeeResult Success(Guid id) => new(HireEmployeeOutcome.Hired, id, null);

    public static HireEmployeeResult DepartmentNotFound() =>
        new(HireEmployeeOutcome.DepartmentNotFound, null, "Departamento não encontrado.");

    /// <summary>
    /// A conta indicada já está ligada a outro colaborador — conflito com o
    /// estado, não pedido malformado (400 seria para um `userId` que nem
    /// sequer parece um identificador).
    /// </summary>
    public static HireEmployeeResult UserAlreadyLinked() =>
        new(HireEmployeeOutcome.UserAlreadyLinked, null, "Esta conta já está associada a outro colaborador.");
}

/// <summary>Acções de `hr` registadas na trilha de auditoria.</summary>
public static class HrAuditActions
{
    public const string EmployeeHired = "hr.employee.hired";
    public const string DepartmentCreated = "hr.department.created";
    public const string PositionCreated = "hr.position.created";
    public const string PositionAssigned = "hr.position.assigned";
    public const string PositionAssignmentSubmitted = "hr.position.assignment_submitted";
    public const string PositionAssignmentApproved = "hr.position.assignment_approved";
    public const string PositionAssignmentRefused = "hr.position.assignment_refused";
    public const string DocumentAttached = "hr.employee.document_attached";
    public const string ContractDrawn = "hr.contract.drawn";
    public const string ContractTerminated = "hr.contract.terminated";
    public const string AttendanceCheckedIn = "hr.attendance.checked_in";
    public const string AttendanceCheckedOut = "hr.attendance.checked_out";
    public const string AbsenceRecorded = "hr.attendance.absence_recorded";
    public const string AbsenceJustified = "hr.attendance.absence_justified";
    public const string BenefitCreated = "hr.benefit.created";
    public const string BenefitEnrolled = "hr.benefit.enrolled";
    public const string BenefitCancelled = "hr.benefit.cancelled";
    public const string JobOpeningOpened = "hr.job_opening.opened";
    public const string JobOpeningClosed = "hr.job_opening.closed";
    public const string CandidateApplied = "hr.candidate.applied";
    public const string CandidateAdvanced = "hr.candidate.advanced";
    public const string CandidateHired = "hr.candidate.hired";
    public const string LifecycleStarted = "hr.lifecycle.started";
    public const string LifecycleTaskCompleted = "hr.lifecycle.task_completed";
    public const string LifecycleCompleted = "hr.lifecycle.completed";
    public const string LeaveRequested = "hr.leave.requested";
    public const string LeaveApproved = "hr.leave.approved";
    public const string LeaveRefused = "hr.leave.refused";
    public const string LeaveCancelled = "hr.leave.cancelled";
}

public static class HrAuditEntityTypes
{
    public const string Employee = "hr.employee";
    public const string Department = "hr.department";
    public const string Position = "hr.position";
    public const string EmploymentContract = "hr.employment_contract";
    public const string Attendance = "hr.attendance_record";
    public const string Benefit = "hr.benefit";
    public const string BenefitEnrolment = "hr.benefit_enrolment";
    public const string JobOpening = "hr.job_opening";
    public const string Candidate = "hr.candidate";
    public const string LifecycleProcess = "hr.lifecycle_process";
    public const string LeaveRequest = "hr.leave_request";
}

