using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

public sealed class ListJobOpenings(IHrStore store)
{
    public async Task<IReadOnlyList<JobOpeningView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var openings = await store.ListJobOpeningsAsync(cancellationToken);

        return [.. openings.Select(o => new JobOpeningView(
            o.Id, o.Title, o.DepartmentId, o.Vacancies, o.Description, o.Requirements, o.Status.ToString()))];
    }
}

public sealed record JobOpeningView(
    Guid OpeningId,
    string Title,
    Guid? DepartmentId,
    int Vacancies,
    string? Description,
    string? Requirements,
    string Status);

public sealed class OpenJobOpening(IHrStore store, IAuditTrail audit)
{
    public async Task<RecruitmentResult> ExecuteAsync(
        string title,
        Guid? departmentId,
        int vacancies,
        string? description,
        string? requirements,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (departmentId is not null && !await store.DepartmentExistsAsync(departmentId.Value, cancellationToken))
        {
            return RecruitmentResult.NotFound("Departamento não encontrado.");
        }

        JobOpening opening;

        try
        {
            opening = JobOpening.Open(title, departmentId, vacancies, description, requirements);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return RecruitmentResult.Rejected(error.Message);
        }

        await store.AddJobOpeningAsync(opening, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.JobOpeningOpened,
                HrAuditEntityTypes.JobOpening,
                opening.Id.ToString(),
                context),
            cancellationToken);

        return RecruitmentResult.Success(opening.Id);
    }
}

public sealed class CloseJobOpening(IHrStore store, IAuditTrail audit)
{
    public async Task<RecruitmentResult> ExecuteAsync(
        Guid openingId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var opening = await store.FindJobOpeningAsync(openingId, cancellationToken);

        if (opening is null)
        {
            return RecruitmentResult.NotFound("Vaga não encontrada.");
        }

        try
        {
            opening.Close();
        }
        catch (InvalidOperationException error)
        {
            return RecruitmentResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.JobOpeningClosed,
                HrAuditEntityTypes.JobOpening,
                opening.Id.ToString(),
                context),
            cancellationToken);

        return RecruitmentResult.Success(opening.Id);
    }
}

public sealed class ListCandidates(IHrStore store)
{
    public async Task<IReadOnlyList<CandidateView>> ExecuteAsync(
        Guid? openingId,
        CancellationToken cancellationToken)
    {
        var candidates = await store.ListCandidatesAsync(openingId, cancellationToken);

        return [.. candidates.Select(c => new CandidateView(
            c.Id, c.JobOpeningId, c.FullName, c.Email, c.Phone,
            c.AppliedOn, c.Stage.ToString(), c.Notes, c.HiredEmployeeId))];
    }
}

public sealed record CandidateView(
    Guid CandidateId,
    Guid OpeningId,
    string FullName,
    string? Email,
    string? Phone,
    DateOnly AppliedOn,
    string Stage,
    string? Notes,
    Guid? HiredEmployeeId);

public sealed class ApplyToJobOpening(IHrStore store, IAuditTrail audit)
{
    public async Task<RecruitmentResult> ExecuteAsync(
        Guid openingId,
        string fullName,
        string? email,
        string? phone,
        DateOnly appliedOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var opening = await store.FindJobOpeningAsync(openingId, cancellationToken);

        if (opening is null)
        {
            return RecruitmentResult.NotFound("Vaga não encontrada.");
        }

        Candidate candidate;

        try
        {
            candidate = Candidate.Apply(opening, fullName, email, phone, appliedOn);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return RecruitmentResult.Rejected(error.Message);
        }

        await store.AddCandidateAsync(candidate, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.CandidateApplied,
                HrAuditEntityTypes.Candidate,
                candidate.Id.ToString(),
                context),
            cancellationToken);

        return RecruitmentResult.Success(candidate.Id);
    }
}

/// <summary>
/// Faz avançar um candidato no funil, ou rejeita-o.
/// </summary>
public sealed class AdvanceCandidate(IHrStore store, IAuditTrail audit)
{
    /// <summary>Fases aceites, derivadas do enum do domínio.</summary>
    public static readonly IReadOnlyList<string> Stages = [.. Enum.GetNames<CandidateStage>()];

    public async Task<RecruitmentResult> ExecuteAsync(
        Guid candidateId,
        string stage,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CandidateStage>(stage, ignoreCase: true, out var target))
        {
            return RecruitmentResult.Rejected(
                $"Fase desconhecida. Esperado: {string.Join(", ", Stages)}.");
        }

        var candidate = await store.FindCandidateAsync(candidateId, cancellationToken);

        if (candidate is null)
        {
            return RecruitmentResult.NotFound("Candidato não encontrado.");
        }

        try
        {
            candidate.AdvanceTo(target);
        }
        catch (InvalidOperationException error)
        {
            return RecruitmentResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.CandidateAdvanced,
                HrAuditEntityTypes.Candidate,
                candidate.Id.ToString(),
                context,
                NewValue: $$"""{"stage":"{{target}}"}"""),
            cancellationToken);

        return RecruitmentResult.Success(candidate.Id);
    }
}

/// <summary>
/// Contrata um candidato, criando o Colaborador e ligando-o à candidatura.
///
/// <para>
/// <strong>É a fronteira entre recrutamento e quadro de pessoal.</strong> Até
/// aqui o candidato é externo; a partir daqui existe um Colaborador, com tudo o
/// que isso implica. Fazê-lo num só caso de uso — em vez de deixar quem usa
/// criar o colaborador à parte e ligá-lo depois — é o que impede candidatos
/// contratados sem colaborador e colaboradores sem rasto de onde vieram.
/// </para>
/// </summary>
public sealed class HireCandidate(IHrStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<RecruitmentResult> ExecuteAsync(
        Guid candidateId,
        Guid? departmentId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var candidate = await store.FindCandidateAsync(candidateId, cancellationToken);

        if (candidate is null)
        {
            return RecruitmentResult.NotFound("Candidato não encontrado.");
        }

        if (departmentId is not null && !await store.DepartmentExistsAsync(departmentId.Value, cancellationToken))
        {
            return RecruitmentResult.NotFound("Departamento não encontrado.");
        }

        // O colaborador é criado antes de a candidatura ser marcada como
        // contratada: se a marca falhasse depois, ficaria um colaborador sem
        // candidatura — que é recuperável. O inverso não seria.
        var employee = Employee.Hire(candidate.FullName, departmentId, userId: null, clock.GetUtcNow());

        try
        {
            candidate.Hire(employee.Id);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return RecruitmentResult.Rejected(error.Message);
        }

        await store.AddEmployeeAsync(employee, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.CandidateHired,
                HrAuditEntityTypes.Candidate,
                candidate.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{employee.Id}}"}"""),
            cancellationToken);

        return RecruitmentResult.Success(employee.Id);
    }
}

public sealed record RecruitmentResult(RecruitmentOutcome Outcome, Guid? Id, string? Error)
{
    public static RecruitmentResult Success(Guid id) => new(RecruitmentOutcome.Done, id, null);

    public static RecruitmentResult NotFound(string reason) => new(RecruitmentOutcome.NotFound, null, reason);

    public static RecruitmentResult Rejected(string reason) => new(RecruitmentOutcome.Rejected, null, reason);
}

public enum RecruitmentOutcome
{
    Done,
    NotFound,
    Rejected,
}
