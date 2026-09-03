using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;

namespace Rivo.Finance.Application;

/// <summary>
/// O contrato publicado de submissão de pagamento para composição
/// (ADR-044). Único consumidor previsto: o Portal do Cliente.
/// </summary>
public sealed class CustomerPayments(SubmitPaymentClaim submit, ListPaymentClaims list) : ICustomerPayments
{
    public Task<SubmitPaymentClaimResult> SubmitClaimAsync(
        Guid customerId,
        Guid salesInvoiceId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        Guid submittedByUserId,
        string? notes,
        CancellationToken cancellationToken) =>
        submit.ExecuteAsync(customerId, salesInvoiceId, amount, paidOn, documentId, submittedByUserId, notes, cancellationToken);

    public Task<IReadOnlyList<PaymentClaimView>> ListMyClaimsAsync(
        Guid customerId, CancellationToken cancellationToken) =>
        list.ExecuteAsync(customerId, status: null, cancellationToken);
}
