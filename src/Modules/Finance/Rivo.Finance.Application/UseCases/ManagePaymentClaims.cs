using Rivo.Audit.Contracts;
using Rivo.Documents.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Submete um pedido de confirmação de pagamento — o cliente diz que pagou
/// e anexa o comprovativo (ADR-044). Não regista nada em dinheiro; só o
/// pedido, para `finance` decidir.
///
/// <para>
/// Devolve directamente <see cref="SubmitPaymentClaimResult"/>, o mesmo tipo
/// que <see cref="ICustomerPayments"/> publica — só há uma forma de contar
/// este desfecho, e é a que o contrato já define.
/// </para>
/// </summary>
public sealed class SubmitPaymentClaim(
    ISalesInvoiceStore store,
    IDocumentCatalogue documents,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<SubmitPaymentClaimResult> ExecuteAsync(
        Guid customerId,
        Guid salesInvoiceId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        Guid submittedByUserId,
        string? notes,
        CancellationToken cancellationToken)
    {
        var factura = await store.FindAsync(salesInvoiceId, cancellationToken);

        // A mesma resposta para "não existe" e "não é deste cliente" — não se
        // revela a outro cliente que uma factura de terceiros existe.
        if (factura is null || factura.CustomerId != customerId)
        {
            return SubmitPaymentClaimResult.InvoiceNotFound();
        }

        if (factura.Status is InvoiceStatus.Cancelled)
        {
            return SubmitPaymentClaimResult.Rejected(
                $"A factura {factura.Number.Formatted} está anulada — não há o que confirmar.");
        }

        // Verificado pelo contrato publicado, não por consulta às tabelas de
        // `documents` — mesmo desenho de `AttachDocumentToVehicle`.
        var descriptor = await documents.FindAsync(documentId, cancellationToken);

        if (descriptor is null)
        {
            return SubmitPaymentClaimResult.DocumentNotFound();
        }

        // A mesma verificação que RegisterReceipt faz — aqui, mais cedo, para
        // não deixar o cliente submeter um pedido que nunca poderia ser
        // confirmado.
        var emAberto = await store.OutstandingAsync(salesInvoiceId, cancellationToken);

        if (amount > emAberto)
        {
            return SubmitPaymentClaimResult.ExceedsOutstanding(
                $"A factura {factura.Number.Formatted} tem {emAberto:N2} em aberto e " +
                $"o pedido é de {amount:N2}.");
        }

        PaymentClaim pedido;

        try
        {
            pedido = PaymentClaim.Submit(
                salesInvoiceId, customerId, amount, paidOn, documentId, submittedByUserId, notes, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return SubmitPaymentClaimResult.Rejected(error.Message);
        }

        await store.AddPaymentClaimAsync(pedido, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentClaimSubmitted,
                FinanceAuditEntityTypes.PaymentClaim,
                pedido.Id.ToString(),
                new AuditContext(submittedByUserId, null, null),
                NewValue: $$"""
                    {"salesInvoiceId":"{{salesInvoiceId}}","amount":{{amount}},"documentId":"{{documentId}}"}
                    """),
            cancellationToken);

        return SubmitPaymentClaimResult.Submitted(pedido.Id);
    }
}

/// <summary>
/// Confirma um pedido — dispara o <see cref="Receipt"/> reutilizando
/// <see cref="RegisterReceipt"/> tal como está, com as mesmas regras. Não
/// duplica nenhuma delas (ADR-044).
/// </summary>
public sealed class ConfirmPaymentClaim(
    ISalesInvoiceStore store,
    RegisterReceipt registerReceipt,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<ReviewPaymentClaimResult> ExecuteAsync(
        Guid claimId,
        Guid reviewedByUserId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindPaymentClaimForUpdateAsync(claimId, cancellationToken);

        if (pedido is null)
        {
            return ReviewPaymentClaimResult.NotFound();
        }

        if (pedido.Status is not PaymentClaimStatus.Pending)
        {
            return ReviewPaymentClaimResult.Rejected(
                $"O pedido já está {pedido.Status} — só um pedido pendente se confirma.");
        }

        var recibo = await registerReceipt.ExecuteAsync(
            string.Empty,
            pedido.PaidOn,
            PaymentMethod.TB,
            [new SettlementInput(pedido.SalesInvoiceId, pedido.Amount)],
            $"Comprovativo submetido pelo cliente em {pedido.SubmittedAt:yyyy-MM-dd}.",
            context,
            cancellationToken);

        if (recibo.Outcome is not RegisterReceiptOutcome.Registered)
        {
            // O pedido fica Pending — quem revê tenta outra vez depois de
            // corrigir o que bloqueou o recibo (série em falta, período
            // fechado). Não se marca como rejeitado por uma falha técnica.
            return ReviewPaymentClaimResult.ReceiptFailed(recibo.Error ?? recibo.Outcome.ToString());
        }

        pedido.Confirm(recibo.ReceiptId!.Value, reviewedByUserId, clock.GetUtcNow());

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentClaimConfirmed,
                FinanceAuditEntityTypes.PaymentClaim,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"receiptId":"{{recibo.ReceiptId}}"}"""),
            cancellationToken);

        return ReviewPaymentClaimResult.Confirmed(recibo.ReceiptId!.Value);
    }
}

/// <summary>Rejeita um pedido — não apaga nada (BR-14); fica como prova de que houve uma tentativa.</summary>
public sealed class RejectPaymentClaim(ISalesInvoiceStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<ReviewPaymentClaimResult> ExecuteAsync(
        Guid claimId,
        string reason,
        Guid reviewedByUserId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindPaymentClaimForUpdateAsync(claimId, cancellationToken);

        if (pedido is null)
        {
            return ReviewPaymentClaimResult.NotFound();
        }

        try
        {
            pedido.Reject(reason, reviewedByUserId, clock.GetUtcNow());
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return ReviewPaymentClaimResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentClaimRejected,
                FinanceAuditEntityTypes.PaymentClaim,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{pedido.RejectionReason}}"}"""),
            cancellationToken);

        return ReviewPaymentClaimResult.RejectedOk();
    }
}

public sealed record ReviewPaymentClaimResult(ReviewPaymentClaimOutcome Outcome, Guid? ReceiptId, string? Error)
{
    public static ReviewPaymentClaimResult Confirmed(Guid receiptId) =>
        new(ReviewPaymentClaimOutcome.Confirmed, receiptId, null);

    public static ReviewPaymentClaimResult RejectedOk() =>
        new(ReviewPaymentClaimOutcome.RejectedOk, null, null);

    public static ReviewPaymentClaimResult NotFound() =>
        new(ReviewPaymentClaimOutcome.NotFound, null, "Pedido não encontrado.");

    /// <summary>Estado do pedido não permite a operação — 409.</summary>
    public static ReviewPaymentClaimResult Rejected(string error) =>
        new(ReviewPaymentClaimOutcome.Rejected, null, error);

    /// <summary>O recibo não pôde ser registado — 409, pedido continua Pending.</summary>
    public static ReviewPaymentClaimResult ReceiptFailed(string error) =>
        new(ReviewPaymentClaimOutcome.ReceiptFailed, null, error);
}

public enum ReviewPaymentClaimOutcome
{
    Confirmed,
    RejectedOk,
    NotFound,
    Rejected,
    ReceiptFailed,
}

/// <summary>Lista pedidos de confirmação — a fila de `finance`, ou "os meus" de um cliente.</summary>
public sealed class ListPaymentClaims(ISalesInvoiceStore store)
{
    public async Task<IReadOnlyList<PaymentClaimView>> ExecuteAsync(
        Guid? customerId, PaymentClaimStatus? status, CancellationToken cancellationToken)
    {
        var pedidos = await store.ListPaymentClaimsAsync(customerId, status, cancellationToken);

        return [.. pedidos.Select(ToView)];
    }

    internal static PaymentClaimView ToView(PaymentClaim claim) =>
        new(claim.Id, claim.SalesInvoiceId, claim.Amount, claim.PaidOn, claim.Status.ToString(),
            claim.RejectionReason, claim.SubmittedAt);
}
