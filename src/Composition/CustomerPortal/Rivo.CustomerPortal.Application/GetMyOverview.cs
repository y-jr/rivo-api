using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// Resolve "o próprio" para o Portal do Cliente (ADR-043) — o cliente
/// ligado à conta autenticada, nunca outro.
///
/// <para>
/// <strong>Camada de composição, não módulo.</strong> Não possui dados
/// próprios: lê `commercial` pelo seu contrato publicado
/// (<see cref="ICustomerDirectory.FindByUserIdAsync"/>) para resolver o
/// cliente, e depois `finance` (<see cref="IReceivablesOverview"/>) pelas
/// variantes por cliente dos mesmos números do Dashboard Executivo — mesmo
/// desenho de <c>Rivo.EmployeePortal.Application.GetMyProfile</c> (ADR-042).
/// </para>
/// </summary>
public sealed class GetMyOverview(ICustomerDirectory customers, IReceivablesOverview receivables)
{
    public async Task<MyOverviewResult> ExecuteAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        string currency,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return MyOverviewResult.Rejected("A data inicial não pode ser posterior à data final.");
        }

        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return MyOverviewResult.NotLinked();
        }

        var receita = await receivables.GetCustomerNetRevenueAsync(
            cliente.CustomerId, from, to, currency, cancellationToken);

        var emAberto = await receivables.GetCustomerOutstandingAsync(
            cliente.CustomerId, currency, cancellationToken);

        var facturas = await receivables.ListCustomerInvoicesAsync(cliente.CustomerId, cancellationToken);

        return MyOverviewResult.Found(new MyOverviewView(
            cliente.CustomerId, cliente.Name, from, to, currency, receita, emAberto, facturas));
    }
}

public enum MyOverviewOutcome
{
    Found,

    /// <summary>
    /// Sem cliente ligado à conta. Traduz-se em 403 na fronteira HTTP — a
    /// conta existe e está autenticada, só não tem "o próprio" que o portal
    /// exista para mostrar (mesma disciplina de ADR-042).
    /// </summary>
    NotLinked,

    Rejected,
}

public sealed record MyOverviewResult(MyOverviewOutcome Outcome, MyOverviewView? Overview, string? Error)
{
    public static MyOverviewResult Found(MyOverviewView overview) => new(MyOverviewOutcome.Found, overview, null);

    public static MyOverviewResult NotLinked() => new(MyOverviewOutcome.NotLinked, null, null);

    public static MyOverviewResult Rejected(string error) => new(MyOverviewOutcome.Rejected, null, error);
}

public sealed record MyOverviewView(
    Guid CustomerId,
    string CustomerName,
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal NetRevenue,
    decimal Outstanding,
    IReadOnlyList<CustomerInvoiceView> Invoices);
