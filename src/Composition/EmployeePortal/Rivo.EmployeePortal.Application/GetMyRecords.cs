using Rivo.Hr.Contracts;
using Rivo.Payroll.Contracts;

namespace Rivo.EmployeePortal.Application;

/// <summary>
/// As quatro leituras do Portal do Colaborador que não são o perfil:
/// assiduidade, férias, documentos e recibos (ADR-062).
///
/// <para>
/// <strong>Uma classe e não quatro, porque partilham a única coisa que
/// importa:</strong> a resolução de «o próprio». Todas começam por traduzir a
/// conta autenticada no colaborador que lhe corresponde, e todas devolvem
/// <see cref="MyRecordsOutcome.NotLinked"/> quando não há nenhum — o mesmo
/// desfecho de <see cref="GetMyProfile"/>, traduzido em 403.
/// </para>
///
/// <para>
/// <strong>Nenhum método aceita um <c>employeeId</c>.</strong> É a propriedade
/// que torna isto seguro, e é estrutural em vez de vigiada: não há parâmetro
/// por onde pedir a assiduidade de outra pessoa, nem sequer por engano. Quem
/// quiser ver os dados de terceiros usa os ecrãs de `hr` e `payroll`, que
/// pedem permissão para isso.
/// </para>
/// </summary>
public sealed class GetMyRecords(
    IEmployeeDirectory employees,
    IEmployeeSelfService selfService,
    IPayrollSelfService payroll)
{
    public Task<MyRecordsResult<OwnAttendanceRecord>> AttendanceAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        ParaOProprioAsync(
            userId, asOf,
            employeeId => selfService.ListAttendanceAsync(employeeId, from, to, cancellationToken),
            cancellationToken);

    public Task<MyRecordsResult<OwnLeaveRequest>> LeaveAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        ParaOProprioAsync(
            userId, asOf,
            employeeId => selfService.ListLeaveAsync(employeeId, cancellationToken),
            cancellationToken);

    public Task<MyRecordsResult<OwnEmployeeDocument>> DocumentsAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        ParaOProprioAsync(
            userId, asOf,
            employeeId => selfService.ListDocumentsAsync(employeeId, cancellationToken),
            cancellationToken);

    public Task<MyRecordsResult<OwnPayslip>> PayslipsAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        ParaOProprioAsync(
            userId, asOf,
            employeeId => payroll.ListPayslipsAsync(employeeId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Resolve o colaborador da conta e só então lê. Escrito uma vez porque a
    /// alternativa — repeti-lo em quatro métodos — é a forma habitual de um
    /// deles ficar a ler sem resolver.
    /// </summary>
    private async Task<MyRecordsResult<T>> ParaOProprioAsync<T>(
        Guid userId,
        DateTimeOffset asOf,
        Func<Guid, Task<IReadOnlyList<T>>> ler,
        CancellationToken cancellationToken)
    {
        var employee = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        if (employee is null)
        {
            return MyRecordsResult<T>.NotLinked();
        }

        return MyRecordsResult<T>.Found(await ler(employee.EmployeeId));
    }
}

public enum MyRecordsOutcome
{
    Found,

    /// <summary>
    /// Sem colaborador ligado à conta. Traduz-se em 403 e não 404: a conta
    /// existe e está autenticada, só não tem «o próprio» que o portal existe
    /// para mostrar (ADR-042).
    /// </summary>
    NotLinked,
}

public sealed record MyRecordsResult<T>(MyRecordsOutcome Outcome, IReadOnlyList<T> Records)
{
    public static MyRecordsResult<T> Found(IReadOnlyList<T> records) => new(MyRecordsOutcome.Found, records);

    public static MyRecordsResult<T> NotLinked() => new(MyRecordsOutcome.NotLinked, []);
}
