using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.UseCases;

/// <summary>
/// Consulta a assiduidade num intervalo de dias.
/// </summary>
public sealed class ListAttendance(IHrStore store)
{
    /// <param name="anomaliesOnly">
    /// Só faltas e atrasos por justificar. É a vista que a fila de RH usa —
    /// filtrada aqui e não no cliente, para não mandar o dia inteiro da empresa
    /// pela rede à procura de meia dúzia de casos.
    /// </param>
    public async Task<IReadOnlyList<AttendanceView>> ExecuteAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId,
        bool anomaliesOnly,
        CancellationToken cancellationToken)
    {
        var records = await store.ListAttendanceAsync(from, to, employeeId, cancellationToken);

        if (anomaliesOnly)
        {
            records = [.. records.Where(r => r.IsAnomaly)];
        }

        return [.. records.Select(r => new AttendanceView(
            r.Id,
            r.EmployeeId,
            r.Day,
            r.CheckedInAt,
            r.CheckedOutAt,
            r.Status.ToString(),
            r.Justification,
            r.ObservedDuration?.TotalHours))];
    }
}

/// <param name="ObservedHours">
/// Duração observada entre entrada e saída. <strong>Não são horas pagas</strong> —
/// converter isto em remuneração é de `payroll`.
/// </param>
public sealed record AttendanceView(
    Guid RecordId,
    Guid EmployeeId,
    DateOnly Day,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CheckedOutAt,
    string Status,
    string? Justification,
    double? ObservedHours);

/// <summary>
/// Marcação de ponto: abre o dia à entrada, fecha-o à saída.
///
/// <para>
/// Uma operação e não duas, porque é assim que um relógio de ponto funciona —
/// a mesma pessoa carrega no mesmo botão. Qual das duas coisas acontece
/// depende de o dia já estar aberto, e essa decisão é do sistema, não de quem
/// marca.
/// </para>
/// </summary>
public sealed class ClockAttendance(IHrStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<ClockResult> ExecuteAsync(
        Guid employeeId,
        DateOnly day,
        bool late,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return ClockResult.EmployeeNotFound();
        }

        var now = clock.GetUtcNow();
        var existing = await store.FindAttendanceAsync(employeeId, day, cancellationToken);

        if (existing is null)
        {
            var record = AttendanceRecord.CheckIn(employeeId, day, now, late);
            await store.AddAttendanceAsync(record, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);

            await RecordAsync(HrAuditActions.AttendanceCheckedIn, record.Id, context, cancellationToken);

            return ClockResult.CheckedIn(record.Id, now);
        }

        try
        {
            existing.CheckOut(now);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // Já saiu, ou o dia foi registado como falta. Recusa-se com a razão
            // em vez de gravar uma segunda marcação em silêncio.
            return ClockResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await RecordAsync(HrAuditActions.AttendanceCheckedOut, existing.Id, context, cancellationToken);

        return ClockResult.CheckedOut(existing.Id, now);
    }

    private Task RecordAsync(string action, Guid recordId, AuditContext context, CancellationToken cancellationToken) =>
        audit.RecordAsync(
            new AuditRecord(action, HrAuditEntityTypes.Attendance, recordId.ToString(), context),
            cancellationToken);
}

public sealed record ClockResult(ClockOutcome Outcome, Guid? RecordId, DateTimeOffset? At, string? Error)
{
    public static ClockResult CheckedIn(Guid id, DateTimeOffset at) =>
        new(ClockOutcome.CheckedIn, id, at, null);

    public static ClockResult CheckedOut(Guid id, DateTimeOffset at) =>
        new(ClockOutcome.CheckedOut, id, at, null);

    public static ClockResult EmployeeNotFound() =>
        new(ClockOutcome.EmployeeNotFound, null, null, "Colaborador não encontrado.");

    public static ClockResult Rejected(string reason) =>
        new(ClockOutcome.Rejected, null, null, reason);
}

public enum ClockOutcome
{
    CheckedIn,
    CheckedOut,
    EmployeeNotFound,
    Rejected,
}

/// <summary>
/// Regista uma ausência, ou justifica uma já registada.
///
/// <para>
/// É a acção que tira um caso da fila de RH.
/// </para>
/// </summary>
public sealed class RecordAbsence(IHrStore store, IAuditTrail audit)
{
    public async Task<AbsenceResult> ExecuteAsync(
        Guid employeeId,
        DateOnly day,
        string? justification,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var employee = await store.FindEmployeeAsync(employeeId, cancellationToken);

        if (employee is null)
        {
            return AbsenceResult.EmployeeNotFound();
        }

        var existing = await store.FindAttendanceAsync(employeeId, day, cancellationToken);

        if (existing is null)
        {
            var record = AttendanceRecord.Absent(employeeId, day, justification);
            await store.AddAttendanceAsync(record, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);

            await Record(HrAuditActions.AbsenceRecorded, record.Id);

            return AbsenceResult.Recorded(record.Id);
        }

        // O dia já existe: isto é uma justificação, não uma falta nova.
        if (string.IsNullOrWhiteSpace(justification))
        {
            return AbsenceResult.Rejected("Este dia já tem marcação. Para o alterar, indique a justificação.");
        }

        try
        {
            existing.Justify(justification);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return AbsenceResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await Record(HrAuditActions.AbsenceJustified, existing.Id);

        return AbsenceResult.Justified(existing.Id);

        Task Record(string action, Guid id) => audit.RecordAsync(
            new AuditRecord(action, HrAuditEntityTypes.Attendance, id.ToString(), context),
            cancellationToken);
    }
}

public sealed record AbsenceResult(AbsenceOutcome Outcome, Guid? RecordId, string? Error)
{
    public static AbsenceResult Recorded(Guid id) => new(AbsenceOutcome.Recorded, id, null);

    public static AbsenceResult Justified(Guid id) => new(AbsenceOutcome.Justified, id, null);

    public static AbsenceResult EmployeeNotFound() =>
        new(AbsenceOutcome.EmployeeNotFound, null, "Colaborador não encontrado.");

    public static AbsenceResult Rejected(string reason) => new(AbsenceOutcome.Rejected, null, reason);
}

public enum AbsenceOutcome
{
    Recorded,
    Justified,
    EmployeeNotFound,
    Rejected,
}
