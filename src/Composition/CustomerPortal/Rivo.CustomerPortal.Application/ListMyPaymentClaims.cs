using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// Os pedidos de confirmação de pagamento do próprio cliente — o estado de
/// cada comprovativo que submeteu (ADR-044).
/// </summary>
public sealed class ListMyPaymentClaims(ICustomerDirectory customers, ICustomerPayments payments)
{
    public async Task<ListMyPaymentClaimsResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return ListMyPaymentClaimsResult.NotLinked();
        }

        var pedidos = await payments.ListMyClaimsAsync(cliente.CustomerId, cancellationToken);

        return ListMyPaymentClaimsResult.Found(pedidos);
    }
}

public enum ListMyPaymentClaimsOutcome
{
    Found,
    NotLinked,
}

public sealed record ListMyPaymentClaimsResult(ListMyPaymentClaimsOutcome Outcome, IReadOnlyList<PaymentClaimView>? Claims)
{
    public static ListMyPaymentClaimsResult Found(IReadOnlyList<PaymentClaimView> claims) =>
        new(ListMyPaymentClaimsOutcome.Found, claims);

    public static ListMyPaymentClaimsResult NotLinked() => new(ListMyPaymentClaimsOutcome.NotLinked, null);
}
