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

/// <summary>
/// Liga uma conta de `identity` a um Colaborador <strong>já admitido</strong>
/// (ADR-051).
///
/// <para>
/// Antes disto o vínculo só se estabelecia na admissão. Passou a fazer falta
/// com o ADR-050: <strong>quem decide uma aprovação tem de ter conta
/// ligada</strong>, e quem já estava admitido sem conta não tinha como ser
/// ligado sem ser readmitido.
/// </para>
///
/// <para>
/// <strong>Não é uma operação corrente de RH.</strong> Exige
/// <c>hr.employees.link_account</c>, que fica fora do perfil HR — criar o
/// vínculo é conceder, indirectamente, o que o Cargo do colaborador confere,
/// incluindo autoridade de aprovação. Mesma razão pela qual
/// <c>hr.positions.write</c> já era só do Admin.
/// </para>
///
/// <para>
/// ⚠ <strong>Não verifica que a conta existe em `identity`.</strong> Fazê-lo
/// exigiria uma dependência nova de `hr` para `identity`, que a regra de
/// fronteiras não deixa introduzir sem justificação — e o precedente é
/// explícito nos dois lados: nem <see cref="HireEmployee"/> nem
/// <c>LinkCustomerAccount</c> a verificam. A consequência é um vínculo para
/// uma conta inexistente: inútil, mas não perigoso — ninguém se autentica com
/// ela, e ocupa o índice único até ser corrigido.
/// </para>
/// </summary>
public sealed class LinkEmployeeAccount(IHrStore store, IAuditTrail audit)
{
    public async Task<LinkEmployeeAccountResult> ExecuteAsync(
        Guid employeeId,
        Guid userId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Recusado antes de tocar no armazenamento, e antes de saber sequer se
        // o colaborador existe: é o caminho de escalada mais directo — ligo a
        // minha conta a um colaborador com Cargo de aprovação e passo a
        // decidir. Mesmo princípio do BR-2, ninguém resolve o seu próprio caso.
        if (context.ActorId is { } actor && actor == userId)
        {
            return LinkEmployeeAccountResult.SelfLinkRefused();
        }

        var colaborador = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (colaborador is null)
        {
            return LinkEmployeeAccountResult.NotFound();
        }

        // Repetível sem erro: ligar de novo a mesma conta produz o estado
        // pretendido na mesma. Sai antes de gravar e de auditar, para a trilha
        // não encher de ligações que não mudaram nada — mesma disciplina de
        // `DeactivateApprovalPolicy`.
        if (colaborador.UserId == userId)
        {
            return LinkEmployeeAccountResult.Success();
        }

        // Religar por cima recusa-se em vez de substituir em silêncio. É aqui
        // que esta rota diverge de `LinkCustomerAccount`, que sobrepõe: no
        // `commercial` a troca reatribui o acesso ao portal do cliente; aqui
        // transferia a identidade com que se aprova. Corrigir um vínculo
        // errado exige desligar primeiro — e desligar ainda não existe, o que
        // está registado como decisão em aberto.
        if (colaborador.UserId is not null)
        {
            return LinkEmployeeAccountResult.EmployeeAlreadyLinked();
        }

        // Uma conta liga-se, no máximo, a um colaborador — é o que o Portal do
        // Colaborador e a resolução de quem decide passaram a confiar (ADR-042,
        // ADR-050). Primeira linha de defesa; o índice único é a segunda.
        if (await store.FindEmployeeByUserIdAsync(userId, cancellationToken) is not null)
        {
            return LinkEmployeeAccountResult.UserAlreadyLinked();
        }

        colaborador.LinkToUser(userId);

        await store.SaveChangesAsync(cancellationToken);

        // `NewValue` guarda a conta ligada de propósito: quem investiga uma
        // decisão de aprovação precisa de saber quando é que aquela conta
        // passou a poder agir por aquela pessoa, e por ordem de quem.
        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeAccountLinked,
                HrAuditEntityTypes.Employee,
                colaborador.Id.ToString(),
                context,
                PreviousValue: null,
                NewValue: $$"""{"userId":"{{userId}}"}"""),
            cancellationToken);

        return LinkEmployeeAccountResult.Success();
    }
}

public enum LinkEmployeeAccountOutcome
{
    Linked,
    NotFound,

    /// <summary>A conta indicada já está ligada a outro colaborador.</summary>
    UserAlreadyLinked,

    /// <summary>Este colaborador já tem outra conta ligada.</summary>
    EmployeeAlreadyLinked,

    /// <summary>
    /// O actor tentou ligar a conta com que está autenticado. Não é conflito
    /// de estado — é a segregação de funções, e traduz-se em 403.
    /// </summary>
    SelfLinkRefused,
}

public sealed record LinkEmployeeAccountResult(LinkEmployeeAccountOutcome Outcome, string? Error)
{
    public bool Succeeded => Outcome == LinkEmployeeAccountOutcome.Linked;

    public static LinkEmployeeAccountResult Success() =>
        new(LinkEmployeeAccountOutcome.Linked, null);

    public static LinkEmployeeAccountResult NotFound() =>
        new(LinkEmployeeAccountOutcome.NotFound, "Colaborador não encontrado.");

    public static LinkEmployeeAccountResult UserAlreadyLinked() =>
        new(LinkEmployeeAccountOutcome.UserAlreadyLinked, "Esta conta já está associada a outro colaborador.");

    public static LinkEmployeeAccountResult EmployeeAlreadyLinked() =>
        new(
            LinkEmployeeAccountOutcome.EmployeeAlreadyLinked,
            "Este colaborador já tem outra conta associada.");

    public static LinkEmployeeAccountResult SelfLinkRefused() =>
        new(
            LinkEmployeeAccountOutcome.SelfLinkRefused,
            "Não pode ligar a sua própria conta a um colaborador. Outra pessoa tem de o fazer.");
}

/// <summary>
/// Desliga a conta de um Colaborador (ADR-052).
///
/// <para>
/// <strong>As decisões de aprovação já tomadas continuam válidas</strong>, e
/// isso não é uma escolha de política — é o que o modelo já dizia.
/// <c>ApprovalDecision</c> guarda <c>DecidedByEmployeeId</c>: o facto gravado
/// é «o colaborador X decidiu», nunca «a conta A decidiu». Desligar a conta
/// não altera quem a pessoa era nem que ela decidiu. Só remove a capacidade
/// de agir <em>daqui para a frente</em>.
/// </para>
///
/// <para>
/// ⚠ <strong>Torna o 409 de <see cref="LinkEmployeeAccount"/> contornável em
/// dois passos.</strong> Quem tiver a permissão pode desligar e voltar a
/// ligar outra conta, conseguindo a transferência que numa só chamada é
/// recusada. Isto é aceite conscientemente: o que a recusa numa chamada
/// impede é a substituição <em>silenciosa</em>, não a deliberada. Dois passos
/// deixam dois registos na trilha, e o do desligar nomeia a conta anterior em
/// <c>PreviousValue</c> — a transferência fica legível, que é o que se quer
/// de uma acção legítima e o que denuncia uma ilegítima.
/// </para>
///
/// <para>
/// Não verifica se o colaborador é aprovador de algum pedido em curso. Fazê-lo
/// exigiria que `hr` visse `approval`, e `hr` define o seu próprio port
/// (<c>IHrApprovalSubmission</c>) precisamente para o ciclo não se formar. O
/// risco — um passo de aprovação cujo aprovador não tem conta — já existe sem
/// isto, porque um colaborador pode ser aprovador sem nunca ter tido conta.
/// </para>
/// </summary>
public sealed class UnlinkEmployeeAccount(IHrStore store, IAuditTrail audit)
{
    public async Task<UnlinkEmployeeAccountResult> ExecuteAsync(
        Guid employeeId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var colaborador = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (colaborador is null)
        {
            return UnlinkEmployeeAccountResult.NotFound();
        }

        // Repetível sem erro, e sem encher a trilha: desligar quem já está
        // desligado produz o estado pretendido na mesma.
        if (colaborador.UserId is not { } contaAnterior)
        {
            return UnlinkEmployeeAccountResult.Success();
        }

        // Sem restrição de auto-desligamento, ao contrário da ligação.
        // Desligar é estritamente uma perda de capacidade, e não encadeia em
        // escalada: para se voltar a ligar a outro colaborador seria preciso
        // ligar a própria conta, que continua recusado.
        colaborador.LinkToUser(null);

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.EmployeeAccountUnlinked,
                HrAuditEntityTypes.Employee,
                colaborador.Id.ToString(),
                context,
                PreviousValue: $$"""{"userId":"{{contaAnterior}}"}""",
                NewValue: null),
            cancellationToken);

        return UnlinkEmployeeAccountResult.Success();
    }
}

public enum UnlinkEmployeeAccountOutcome
{
    Unlinked,
    NotFound,
}

public sealed record UnlinkEmployeeAccountResult(UnlinkEmployeeAccountOutcome Outcome, string? Error)
{
    public bool Succeeded => Outcome == UnlinkEmployeeAccountOutcome.Unlinked;

    public static UnlinkEmployeeAccountResult Success() =>
        new(UnlinkEmployeeAccountOutcome.Unlinked, null);

    public static UnlinkEmployeeAccountResult NotFound() =>
        new(UnlinkEmployeeAccountOutcome.NotFound, "Colaborador não encontrado.");
}

/// <summary>Acções de `hr` registadas na trilha de auditoria.</summary>
public static class HrAuditActions
{
    public const string EmployeeHired = "hr.employee.hired";

    /// <summary>
    /// Uma conta passou a agir em nome de um colaborador (ADR-051). É evento
    /// de segurança, não administrativo: desde o ADR-050 é este vínculo que
    /// determina quem pode decidir aprovações.
    /// </summary>
    public const string EmployeeAccountLinked = "hr.employee.account_linked";

    /// <summary>
    /// Uma conta deixou de agir em nome de um colaborador (ADR-052). Guarda a
    /// conta removida em <c>PreviousValue</c>: é o que torna legível uma
    /// transferência feita em dois passos, desligar seguido de ligar.
    /// </summary>
    public const string EmployeeAccountUnlinked = "hr.employee.account_unlinked";
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

