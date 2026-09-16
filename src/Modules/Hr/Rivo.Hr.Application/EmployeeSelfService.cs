using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;

namespace Rivo.Hr.Application;

/// <summary>
/// Implementa <see cref="IEmployeeSelfService"/> — o que o Portal do
/// Colaborador lê sobre o próprio (ADR-062).
///
/// <para>
/// <strong>Reutiliza os casos de uso administrativos, e não consulta a base
/// por sua conta.</strong> São as mesmas leituras que o ecrã de RH faz, com o
/// filtro por colaborador que elas já aceitam. Escrever consultas paralelas
/// aqui criaria duas definições da mesma coisa, e a segunda ficaria para trás
/// no dia em que a primeira mudasse.
/// </para>
///
/// <para>
/// <strong>O <c>employeeId</c> chega já resolvido, e isso é a segurança
/// toda.</strong> Quem chama traduziu a conta autenticada no colaborador que
/// lhe corresponde (ADR-042); esta classe nunca recebe um identificador do
/// cliente HTTP, e por isso não há por onde pedir a assiduidade de outra
/// pessoa.
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
        // `anomaliesOnly: false` — o próprio vê o mês todo, e não só os dias
        // problemáticos. Filtrar anomalias é ferramenta de quem gere, não de
        // quem quer conferir o seu registo.
        var registos = await attendance.ExecuteAsync(from, to, employeeId, anomaliesOnly: false, cancellationToken);

        return
        [
            .. registos.Select(r => new OwnAttendanceRecord(
                r.RecordId,
                r.Day,
                r.CheckedInAt,
                r.CheckedOutAt,
                r.Status,
                r.Justification,
                r.ObservedHours))
        ];
    }

    public async Task<IReadOnlyList<OwnLeaveRequest>> ListLeaveAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var pedidos = await leave.ExecuteAsync(employeeId, cancellationToken);

        return
        [
            .. pedidos
                // Do mais recente para o mais antigo: quem abre o portal quer
                // saber do pedido que fez esta semana, não do de há dois anos.
                .OrderByDescending(l => l.StartsOn)
                .Select(l => new OwnLeaveRequest(
                    l.LeaveId,
                    l.Type,
                    l.StartsOn,
                    l.EndsOn,
                    l.CalendarDays,
                    l.Status,
                    l.Reason,
                    l.ApprovalRequestId))
        ];
    }

    public async Task<IReadOnlyList<OwnEmployeeDocument>> ListDocumentsAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var ficheiros = await documents.ExecuteAsync(employeeId, cancellationToken);

        return
        [
            .. ficheiros.Select(d => new OwnEmployeeDocument(
                d.DocumentId,
                d.Category,
                d.FileName,
                d.ContentType,
                d.SizeInBytes,
                d.AttachedAt))
        ];
    }
}
