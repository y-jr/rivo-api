using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;

namespace Rivo.Hr.Application;

/// <summary>
/// Implementa <see cref="IEmployeeSelfService"/> — o que um colaborador vê
/// sobre si próprio (ADR-042).
///
/// <para>
/// <strong>Delega nos casos de uso que já existem</strong> em vez de voltar a
/// ler o armazenamento. Uma segunda leitura seria uma segunda oportunidade de
/// as duas divergirem: se a projecção de férias mudar, muda num sítio só, e
/// esta vista acompanha sem ninguém se lembrar dela.
/// </para>
///
/// <para>
/// A tradução para os tipos do contrato existe porque os dois lados podem
/// divergir — a vista interna pode ganhar campos que o portal não deve ver, e
/// é para isso que o contrato é estreito (ADR-010).
/// </para>
/// </summary>
public sealed class EmployeeSelfService(
    ListAttendance attendance,
    ListLeave leave,
    ListEmployeeDocuments documents) : IEmployeeSelfService
{
    public async Task<IReadOnlyList<OwnAttendanceRecord>> ListAttendanceAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // `anomaliesOnly: false` — o colaborador vê o seu mês inteiro, e não
        // só as faltas. O filtro de anomalias existe para a fila de RH, que
        // procura os casos por tratar; quem consulta a própria assiduidade
        // quer o registo todo.
        var registos = await attendance.ExecuteAsync(
            from, to, employeeId, anomaliesOnly: false, cancellationToken);

        return
        [
            .. registos.Select(r => new OwnAttendanceRecord(
                r.RecordId,
                r.Day,
                r.CheckedInAt,
                r.CheckedOutAt,
                r.Status,
                r.Justification,
                r.ObservedHours)),
        ];
    }

    public async Task<IReadOnlyList<OwnLeaveRequest>> ListLeaveAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var pedidos = await leave.ExecuteAsync(employeeId, cancellationToken);

        return
        [
            .. pedidos.Select(l => new OwnLeaveRequest(
                l.LeaveId,
                l.Type,
                l.StartsOn,
                l.EndsOn,
                l.CalendarDays,
                l.Status,
                l.Reason,
                l.ApprovalRequestId)),
        ];
    }

    public async Task<IReadOnlyList<OwnDocument>> ListDocumentsAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var ligacoes = await documents.ExecuteAsync(employeeId, cancellationToken);

        return
        [
            .. ligacoes.Select(d => new OwnDocument(
                d.DocumentId,
                d.Category,
                d.FileName,
                d.ContentType,
                d.SizeInBytes,
                d.AttachedAt)),
        ];
    }
}
