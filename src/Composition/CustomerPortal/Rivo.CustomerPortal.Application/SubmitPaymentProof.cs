using Rivo.Commercial.Contracts;
using Rivo.Finance.Contracts;

namespace Rivo.CustomerPortal.Application;

/// <summary>
/// O cliente submete o comprovativo de uma transferência bancária — terceiro
/// caso de uso do Portal do Cliente (ADR-044), mesma resolução de "o
/// próprio" de <see cref="GetMyOverview"/>. Delega tudo o resto a `finance`
/// através de <see cref="ICustomerPayments"/>: esta camada não valida nada
/// que `finance` já vá validar.
/// </summary>
public sealed class SubmitPaymentProof(ICustomerDirectory customers, ICustomerPayments payments)
{
    public async Task<SubmitPaymentProofResult> ExecuteAsync(
        Guid userId,
        Guid salesInvoiceId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var cliente = await customers.FindByUserIdAsync(userId, cancellationToken);

        if (cliente is null)
        {
            return SubmitPaymentProofResult.NotLinked();
        }

        var resultado = await payments.SubmitClaimAsync(
            cliente.CustomerId, salesInvoiceId, amount, paidOn, documentId, userId, notes, cancellationToken);

        return SubmitPaymentProofResult.From(resultado);
    }
}

public enum SubmitPaymentProofOutcome
{
    Submitted,
    NotLinked,
    InvoiceNotFound,
    DocumentNotFound,
    ExceedsOutstanding,
    Rejected,
}

public sealed record SubmitPaymentProofResult(SubmitPaymentProofOutcome Outcome, Guid? ClaimId, string? Error)
{
    public static SubmitPaymentProofResult NotLinked() => new(SubmitPaymentProofOutcome.NotLinked, null, null);

    public static SubmitPaymentProofResult From(SubmitPaymentClaimResult inner) => new(
        inner.Outcome switch
        {
            SubmitPaymentClaimOutcome.Submitted => SubmitPaymentProofOutcome.Submitted,
            SubmitPaymentClaimOutcome.InvoiceNotFound => SubmitPaymentProofOutcome.InvoiceNotFound,
            SubmitPaymentClaimOutcome.DocumentNotFound => SubmitPaymentProofOutcome.DocumentNotFound,
            SubmitPaymentClaimOutcome.ExceedsOutstanding => SubmitPaymentProofOutcome.ExceedsOutstanding,
            _ => SubmitPaymentProofOutcome.Rejected,
        },
        inner.ClaimId,
        inner.Error);
}
