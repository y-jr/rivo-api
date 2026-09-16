using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Contracts;

namespace Rivo.Payroll.Application;

/// <summary>
/// Implementa <see cref="IPayrollSelfService"/> — os recibos que o Portal do
/// Colaborador mostra ao próprio (ADR-062).
///
/// <para>
/// <strong>O filtro de «só aprovadas» está na consulta</strong>
/// (<see cref="IPayrollRunStore.ListApprovedItemsForEmployeeAsync"/>) e não
/// aqui. É deliberado: uma regra que vive na consulta não pode ser esquecida
/// por um chamador novo, e esta é uma regra que, esquecida, mostra a alguém um
/// vencimento que ainda vai mudar.
/// </para>
/// </summary>
public sealed class PayrollSelfService(IPayrollRunStore store) : IPayrollSelfService
{
    public async Task<IReadOnlyList<OwnPayslip>> ListPayslipsAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var aprovados = await store.ListApprovedItemsForEmployeeAsync(employeeId, cancellationToken);

        if (aprovados.Count == 0)
        {
            return [];
        }

        // Em lote: um recibo por mês são doze consultas por ano de histórico, e
        // o portal lê o histórico todo de cada vez que abre.
        var documentos = await store.ListDocumentsForItemsAsync(
            [.. aprovados.Select(a => a.Item.Id)], cancellationToken);

        // O mais recente de cada item, quando há mais do que um. A consulta já
        // vem ordenada por data de anexação descendente.
        var porItem = documentos
            .GroupBy(d => d.PayrollItemId)
            .ToDictionary(g => g.Key, g => g.First().DocumentId);

        return
        [
            .. aprovados.Select(a => new OwnPayslip(
                a.Item.Id,
                a.Item.RunId,
                a.Year,
                a.Month,
                a.Item.GrossSalary,
                a.Item.FoodAllowance,
                a.Item.TransportAllowance,
                a.Item.VacationAllowance,
                a.Item.ChristmasAllowance,
                a.Item.NetSalary,
                a.Item.WithholdingTax,
                a.Item.SocialSecurityContribution,
                porItem.TryGetValue(a.Item.Id, out var documentId) ? documentId : null))
        ];
    }
}
