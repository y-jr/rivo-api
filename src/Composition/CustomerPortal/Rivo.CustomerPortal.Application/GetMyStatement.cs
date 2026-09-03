using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// Extracto de conta corrente do próprio cliente — segundo caso de uso do
/// Portal do Cliente (ADR-043), mesma resolução de "o próprio" de
/// <see cref="GetMyOverview"/>.
/// </summary>
public sealed class GetMyStatement(ICustomerDirectory customers, IReceivablesOverview receivables)
{
    public async Task<MyStatementResult> ExecuteAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        string currency,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return MyStatementResult.Rejected("A data inicial não pode ser posterior à data final.");
        }

        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return MyStatementResult.NotLinked();
        }

        var extracto = await receivables.GetCustomerStatementAsync(
            cliente.CustomerId, from, to, currency, cancellationToken);

        return MyStatementResult.Found(extracto);
    }
}

public enum MyStatementOutcome
{
    Found,
    NotLinked,
    Rejected,
}

public sealed record MyStatementResult(MyStatementOutcome Outcome, CustomerStatementView? Statement, string? Error)
{
    public static MyStatementResult Found(CustomerStatementView statement) =>
        new(MyStatementOutcome.Found, statement, null);

    public static MyStatementResult NotLinked() => new(MyStatementOutcome.NotLinked, null, null);

    public static MyStatementResult Rejected(string error) => new(MyStatementOutcome.Rejected, null, error);
}
