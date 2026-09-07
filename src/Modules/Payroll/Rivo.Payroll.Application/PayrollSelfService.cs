using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Contracts;

namespace Rivo.Payroll.Application;

/// <summary>
/// Implementa <see cref="IPayrollSelfService"/> — os recibos do próprio
/// colaborador (ADR-042).
///
/// <para>
/// <strong>O filtro de "só aprovadas" vive no armazenamento</strong>, e não
/// aqui: um item de folha em rascunho não deve sequer chegar a esta camada.
/// Filtrar depois de ler daria a quem mexesse neste ficheiro a hipótese de
/// remover o filtro sem perceber que estava a expor números por confirmar.
/// </para>
/// </summary>
public sealed class PayrollSelfService(IPayrollRunStore store) : IPayrollSelfService
{
    public async Task<IReadOnlyList<OwnPayslip>> ListPayslipsAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var recibos = await store.ListApprovedPayslipsAsync(employeeId, cancellationToken);

        return
        [
            .. recibos.Select(r => new OwnPayslip(
                r.Item.Id,
                r.Run.Id,
                r.Run.Year,
                r.Run.Month,
                r.Item.GrossSalary,
                r.Item.FoodAllowance,
                r.Item.TransportAllowance,
                r.Item.VacationAllowance,
                r.Item.ChristmasAllowance,
                r.Item.NetSalary,
                r.Item.WithholdingTax,
                r.Item.SocialSecurityContribution,
                r.DocumentId)),
        ];
    }
}
