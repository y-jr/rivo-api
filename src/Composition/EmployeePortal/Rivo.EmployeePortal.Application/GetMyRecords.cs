using Rivo.Hr.Contracts;

namespace Rivo.EmployeePortal.Application;

/// <summary>
/// As três leituras do próprio colaborador — assiduidade, férias e documentos
/// (ADR-042).
///
/// <para>
/// <strong>Um ficheiro e três casos de uso, e não três ficheiros.</strong> Os
/// três fazem literalmente a mesma coisa: resolvem "o próprio" a partir da
/// conta e delegam num contrato de `hr`. Separá-los daria três cópias da
/// mesma resolução, e é aí que uma delas viria a divergir.
/// </para>
///
/// <para>
/// <strong>Nunca aceitam <c>employeeId</c>.</strong> Devolvem sempre e só o
/// colaborador da conta que chama — para ver dados de terceiros, os endpoints
/// de `hr` com as suas permissões continuam a ser o caminho. É a mesma
/// disciplina do Portal do Cliente, e a razão de o portal não ter permissão
/// própria: "o próprio" não é uma operação que se atribua a um perfil.
/// </para>
/// </summary>
public sealed class GetMyAttendance(IEmployeeDirectory employees, IEmployeeSelfService self)
{
    public async Task<MyRecordsResult<OwnAttendanceRecord>> ExecuteAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return MyRecordsResult<OwnAttendanceRecord>.Rejected(
                "A data inicial é posterior à data final.");
        }

        var colaborador = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        if (colaborador is null)
        {
            return MyRecordsResult<OwnAttendanceRecord>.NotLinked();
        }

        var registos = await self.ListAttendanceAsync(
            colaborador.EmployeeId, from, to, cancellationToken);

        return MyRecordsResult<OwnAttendanceRecord>.Found(registos);
    }
}

public sealed class GetMyLeave(IEmployeeDirectory employees, IEmployeeSelfService self)
{
    public async Task<MyRecordsResult<OwnLeaveRequest>> ExecuteAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var colaborador = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        if (colaborador is null)
        {
            return MyRecordsResult<OwnLeaveRequest>.NotLinked();
        }

        var pedidos = await self.ListLeaveAsync(colaborador.EmployeeId, cancellationToken);

        return MyRecordsResult<OwnLeaveRequest>.Found(pedidos);
    }
}

public sealed class GetMyDocuments(IEmployeeDirectory employees, IEmployeeSelfService self)
{
    public async Task<MyRecordsResult<OwnDocument>> ExecuteAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var colaborador = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        if (colaborador is null)
        {
            return MyRecordsResult<OwnDocument>.NotLinked();
        }

        var ligacoes = await self.ListDocumentsAsync(colaborador.EmployeeId, cancellationToken);

        return MyRecordsResult<OwnDocument>.Found(ligacoes);
    }
}

/// <summary>
/// O desfecho de uma leitura do próprio.
///
/// <para>
/// <strong>Genérico porque os três desfechos são os mesmos</strong> — achou,
/// não há vínculo, ou o pedido não está bem formado. O que muda entre eles é
/// só o que vem dentro.
/// </para>
/// </summary>
public sealed record MyRecordsResult<T>(
    MyRecordsOutcome Outcome,
    IReadOnlyList<T>? Records,
    string? Error)
{
    public static MyRecordsResult<T> Found(IReadOnlyList<T> records) =>
        new(MyRecordsOutcome.Found, records, null);

    public static MyRecordsResult<T> NotLinked() =>
        new(MyRecordsOutcome.NotLinked, null, null);

    public static MyRecordsResult<T> Rejected(string error) =>
        new(MyRecordsOutcome.Rejected, null, error);
}

public enum MyRecordsOutcome
{
    Found,

    /// <summary>
    /// Sem colaborador ligado à conta. Traduz-se em <c>403</c> e não em
    /// <c>404</c>: a conta existe e está autenticada, só não tem "o próprio"
    /// que o portal existe para mostrar (ADR-042).
    /// </summary>
    NotLinked,

    /// <summary>Pedido mal formado — hoje, só a janela de datas invertida.</summary>
    Rejected,
}
