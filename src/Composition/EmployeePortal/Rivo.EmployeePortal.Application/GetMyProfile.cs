using Rivo.Hr.Contracts;

namespace Rivo.EmployeePortal.Application;

/// <summary>
/// Resolve "o próprio" para o Portal do Colaborador (ADR-042) — o
/// colaborador ligado à conta autenticada, nunca outro.
///
/// <para>
/// <strong>Camada de composição, não módulo.</strong> Não possui dados
/// próprios: lê `hr` pelo seu contrato publicado
/// (<see cref="IEmployeeDirectory.FindByUserIdAsync"/>), que por sua vez
/// nunca devolve mais de um colaborador por conta (índice único em
/// `HrDbContext`, ADR-042).
/// </para>
/// </summary>
public sealed class GetMyProfile(IEmployeeDirectory employees)
{
    public async Task<MyProfileResult> ExecuteAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var employee = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        return employee is null ? MyProfileResult.NotLinked() : MyProfileResult.Found(ToView(employee));
    }

    private static MyProfileView ToView(EmployeeReference employee) => new(
        employee.EmployeeId,
        employee.DisplayName,
        employee.Status.ToString(),
        employee.DepartmentId,
        employee.CurrentPosition is null
            ? null
            : new MyPositionView(employee.CurrentPosition.Name, employee.CurrentPosition.GrantsApprovalAuthority));
}

public enum MyProfileOutcome
{
    Found,

    /// <summary>
    /// Sem colaborador ligado à conta. Traduz-se em 403 na fronteira HTTP —
    /// não 404: a conta existe e está autenticada, só não tem "o próprio"
    /// que o portal exista para mostrar (ADR-042, "nunca tenta adivinhar").
    /// </summary>
    NotLinked,
}

public sealed record MyProfileResult(MyProfileOutcome Outcome, MyProfileView? Profile)
{
    public static MyProfileResult Found(MyProfileView profile) => new(MyProfileOutcome.Found, profile);

    public static MyProfileResult NotLinked() => new(MyProfileOutcome.NotLinked, null);
}

public sealed record MyProfileView(
    Guid EmployeeId,
    string DisplayName,
    string Status,
    Guid? DepartmentId,
    MyPositionView? CurrentPosition);

public sealed record MyPositionView(string Name, bool GrantsApprovalAuthority);
