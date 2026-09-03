namespace Rivo.Finance.Domain;

/// <summary>
/// Pedido de confirmação de pagamento — o cliente diz que pagou uma factura
/// por transferência bancária e anexa o comprovativo; `finance` confirma ou
/// rejeita (ADR-044).
///
/// <para>
/// <strong>Não é o recebimento em si.</strong> Confirmar não faz este
/// agregado guardar dinheiro nenhum — dispara o <see cref="Receipt"/> que já
/// existe, com as mesmas regras (moeda, cliente, "não recebe mais do que
/// está em aberto"). O que este agregado guarda é só o pedido e o rasto do
/// comprovativo até alguém decidir.
/// </para>
/// </summary>
public sealed class PaymentClaim
{
    private PaymentClaim(
        Guid id,
        Guid salesInvoiceId,
        Guid customerId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        Guid submittedByUserId,
        string? notes,
        DateTimeOffset submittedAt)
    {
        Id = id;
        SalesInvoiceId = salesInvoiceId;
        CustomerId = customerId;
        Amount = amount;
        PaidOn = paidOn;
        DocumentId = documentId;
        SubmittedByUserId = submittedByUserId;
        Notes = notes;
        SubmittedAt = submittedAt;
        Status = PaymentClaimStatus.Pending;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PaymentClaim()
    {
    }

    public Guid Id { get; private set; }

    public Guid SalesInvoiceId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>A data que o cliente diz ter pago — a que o recibo, quando confirmado, herda.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>O comprovativo, em `documents` (ADR-009). Só a referência — o ficheiro não vive aqui.</summary>
    public Guid DocumentId { get; private set; }

    public Guid SubmittedByUserId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public PaymentClaimStatus Status { get; private set; }

    /// <summary>Preenchido só quando confirmado — o recibo que este pedido gerou.</summary>
    public Guid? ReceiptId { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static PaymentClaim Submit(
        Guid salesInvoiceId,
        Guid customerId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        Guid submittedByUserId,
        string? notes,
        DateTimeOffset at)
    {
        // Zero ou negativo não é um pagamento — é o mesmo limite de
        // ReceiptLine.Create, pela mesma razão.
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A quantia paga é maior que zero.");
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um pedido de confirmação vem sempre com o comprovativo anexado.", nameof(documentId));
        }

        return new PaymentClaim(
            Guid.CreateVersion7(),
            salesInvoiceId,
            customerId,
            amount,
            paidOn,
            documentId,
            submittedByUserId,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            at);
    }

    /// <param name="receiptId">
    /// O recibo já registado do lado de fora — este agregado não sabe
    /// registar recibos, só sabe que este pedido levou a um.
    /// </param>
    public void Confirm(Guid receiptId, Guid reviewedByUserId, DateTimeOffset at)
    {
        if (Status is not PaymentClaimStatus.Pending)
        {
            throw new InvalidOperationException(
                $"O pedido já está {Status} — só um pedido pendente se confirma.");
        }

        Status = PaymentClaimStatus.Confirmed;
        ReceiptId = receiptId;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = at;
    }

    /// <summary>
    /// Rejeita — não apaga (BR-14). O cliente vê o motivo e pode submeter um
    /// pedido novo; este fica como está, prova de que houve uma tentativa.
    /// </summary>
    public void Reject(string reason, Guid reviewedByUserId, DateTimeOffset at)
    {
        if (Status is not PaymentClaimStatus.Pending)
        {
            throw new InvalidOperationException(
                $"O pedido já está {Status} — só um pedido pendente se rejeita.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Rejeitar exige motivo — o cliente vai ver isto, e \"não\" sozinho não ajuda.",
                nameof(reason));
        }

        Status = PaymentClaimStatus.Rejected;
        RejectionReason = reason.Trim();
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = at;
    }
}

public enum PaymentClaimStatus
{
    Pending,
    Confirmed,
    Rejected,
}
