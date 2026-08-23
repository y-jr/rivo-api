using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

public sealed class ListBenefits(IHrStore store)
{
    public async Task<IReadOnlyList<BenefitView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var benefits = await store.ListBenefitsAsync(cancellationToken);

        return [.. benefits.Select(b => new BenefitView(
            b.Id, b.Name, b.Kind, b.MonthlyValue, b.Currency, b.Description, b.IsActive))];
    }
}

public sealed record BenefitView(
    Guid BenefitId,
    string Name,
    string Kind,
    decimal MonthlyValue,
    string Currency,
    string? Description,
    bool IsActive);

public sealed class CreateBenefit(IHrStore store, IAuditTrail audit)
{
    public async Task<CreateBenefitResult> ExecuteAsync(
        string name,
        string kind,
        decimal monthlyValue,
        string currency,
        string? description,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Benefit benefit;

        try
        {
            benefit = Benefit.Create(name, kind, monthlyValue, currency, description);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return CreateBenefitResult.Rejected(error.Message);
        }

        await store.AddBenefitAsync(benefit, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.BenefitCreated,
                HrAuditEntityTypes.Benefit,
                benefit.Id.ToString(),
                context),
            cancellationToken);

        return CreateBenefitResult.Success(benefit.Id);
    }
}

public sealed record CreateBenefitResult(bool Succeeded, Guid? BenefitId, string? Error)
{
    public static CreateBenefitResult Success(Guid id) => new(true, id, null);

    public static CreateBenefitResult Rejected(string reason) => new(false, null, reason);
}

public sealed class ListBenefitEnrolments(IHrStore store)
{
    public async Task<IReadOnlyList<BenefitEnrolmentView>> ExecuteAsync(
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var enrolments = await store.ListEnrolmentsAsync(employeeId, cancellationToken);

        return [.. enrolments.Select(e => new BenefitEnrolmentView(
            e.Id, e.EmployeeId, e.BenefitId, e.StartsOn, e.CancelledOn, e.Status.ToString()))];
    }
}

public sealed record BenefitEnrolmentView(
    Guid EnrolmentId,
    Guid EmployeeId,
    Guid BenefitId,
    DateOnly StartsOn,
    DateOnly? CancelledOn,
    string Status);

/// <summary>
/// Adere um colaborador a um benefício.
///
/// <para>
/// A regra que precisa do repositório é a duplicação: <strong>não se adere
/// duas vezes ao mesmo benefício enquanto a primeira adesão estiver
/// activa</strong>. Aderir a um benefício descontinuado é recusado pelo
/// domínio, que vê o benefício.
/// </para>
/// </summary>
public sealed class EnrolInBenefit(IHrStore store, IAuditTrail audit)
{
    public async Task<EnrolResult> ExecuteAsync(
        Guid employeeId,
        Guid benefitId,
        DateOnly startsOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return EnrolResult.NotFound("Colaborador não encontrado.");
        }

        var benefit = await store.FindBenefitAsync(benefitId, cancellationToken);

        if (benefit is null)
        {
            return EnrolResult.NotFound("Benefício não encontrado.");
        }

        var existing = await store.ListEnrolmentsAsync(employeeId, cancellationToken);

        if (existing.Any(e => e.BenefitId == benefitId && e.Status == BenefitEnrolmentStatus.Active))
        {
            return EnrolResult.Rejected("O colaborador já tem este benefício activo.");
        }

        BenefitEnrolment enrolment;

        try
        {
            enrolment = BenefitEnrolment.Enrol(employeeId, benefit, startsOn);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return EnrolResult.Rejected(error.Message);
        }

        await store.AddEnrolmentAsync(enrolment, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.BenefitEnrolled,
                HrAuditEntityTypes.BenefitEnrolment,
                enrolment.Id.ToString(),
                context,
                NewValue: $$"""{"employeeId":"{{employeeId}}","benefitId":"{{benefitId}}"}"""),
            cancellationToken);

        return EnrolResult.Success(enrolment.Id);
    }
}

public sealed class CancelBenefitEnrolment(IHrStore store, IAuditTrail audit)
{
    public async Task<EnrolResult> ExecuteAsync(
        Guid enrolmentId,
        DateOnly on,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var enrolment = await store.FindEnrolmentAsync(enrolmentId, cancellationToken);

        if (enrolment is null)
        {
            return EnrolResult.NotFound("Adesão não encontrada.");
        }

        try
        {
            enrolment.Cancel(on);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return EnrolResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.BenefitCancelled,
                HrAuditEntityTypes.BenefitEnrolment,
                enrolment.Id.ToString(),
                context),
            cancellationToken);

        return EnrolResult.Success(enrolment.Id);
    }
}

public sealed record EnrolResult(EnrolOutcome Outcome, Guid? EnrolmentId, string? Error)
{
    public static EnrolResult Success(Guid id) => new(EnrolOutcome.Done, id, null);

    public static EnrolResult NotFound(string reason) => new(EnrolOutcome.NotFound, null, reason);

    public static EnrolResult Rejected(string reason) => new(EnrolOutcome.Rejected, null, reason);
}

public enum EnrolOutcome
{
    Done,
    NotFound,
    Rejected,
}
