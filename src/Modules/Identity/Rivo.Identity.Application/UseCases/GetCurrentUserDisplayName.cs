using Rivo.Hr.Contracts;

namespace Rivo.Identity.Application.UseCases;

/// <summary>
/// Resolve o nome a mostrar da conta autenticada, para o cliente se orientar
/// em <c>GET /identity/me</c>.
///
/// <para>
/// Lê `hr` pelo contrato publicado
/// (<see cref="IEmployeeDirectory.FindByUserIdAsync"/>), como o Portal do
/// Colaborador já faz (<c>GetMyProfile</c>) — nunca cópia o nome para uma
/// tabela própria (BR-18). Nem toda a conta tem colaborador ligado (ADR-042);
/// nesse caso o nome fica por preencher em vez de se inventar um.
/// </para>
/// </summary>
public sealed class GetCurrentUserDisplayName(IEmployeeDirectory employees)
{
    public async Task<string?> ExecuteAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var employee = await employees.FindByUserIdAsync(userId, asOf, cancellationToken);

        return employee?.DisplayName;
    }
}
