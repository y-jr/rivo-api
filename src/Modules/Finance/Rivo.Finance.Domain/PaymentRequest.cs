namespace Rivo.Finance.Domain;

/// <summary>
/// Pedido de pagamento.
///
/// <para>
/// <strong>Não tem passos de aprovação, e é o ponto todo.</strong>
/// `modules/finance.md` proíbe expressamente embutir workflow aqui — corrige o
/// anti-padrão do protótipo, onde `payment_requests` tinha o workflow na própria
/// tabela. A decisão vive em `approval`; o que fica aqui é só o identificador do
/// processo.
/// </para>
///
/// <para>
/// Por isso os estados são <strong>dois</strong>: elegível e executado (mais
/// cancelado, que é BR-14). Não há "pendente de aprovação" — isso é estado do
/// processo, não do pedido, e guardá-lo aqui seria copiar para `finance` uma
/// verdade que é de `approval` e que fica obsoleta em silêncio.
/// </para>
/// </summary>
public sealed class PaymentRequest
{
    private PaymentRequest(
        Guid id,
        Guid purchaseInvoiceId,
        string supplierInvoiceNumber,
        PayeeParty payee,
        decimal amount,
        string currency,
        Guid requestedByEmployeeId,
        Guid approvalRequestId,
        DateOnly requestedOn,
        string? notes)
    {
        Id = id;
        PurchaseInvoiceId = purchaseInvoiceId;
        SupplierInvoiceNumber = supplierInvoiceNumber;
        Payee = payee;
        Amount = amount;
        Currency = currency;
        RequestedByEmployeeId = requestedByEmployeeId;
        ApprovalRequestId = approvalRequestId;
        RequestedOn = requestedOn;
        Notes = notes;
        Status = PaymentRequestStatus.Eligible;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private PaymentRequest()
    {
        SupplierInvoiceNumber = string.Empty;
        Payee = null!;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseInvoiceId { get; private set; }

    public string SupplierInvoiceNumber { get; private set; }

    public PayeeParty Payee { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>
    /// Quem pediu, como Colaborador. É contra ele que BR-2 é verificada em
    /// `approval` — quem submete não decide.
    /// </summary>
    public Guid RequestedByEmployeeId { get; private set; }

    /// <summary>
    /// O processo em `approval`.
    ///
    /// <para>
    /// <strong>É um ponteiro, não uma cópia do estado.</strong> Saber se está
    /// aprovado exige perguntar, e é isso que torna BR-5 possível: a decisão é
    /// revalidada no momento da execução, não lida de um campo que alguém
    /// escreveu há uma semana.
    /// </para>
    /// </summary>
    public Guid ApprovalRequestId { get; private set; }

    public DateOnly RequestedOn { get; private set; }

    public string? Notes { get; private set; }

    public PaymentRequestStatus Status { get; private set; }

    // ---- execução ----

    public Guid? ExecutedFromAccountId { get; private set; }

    /// <summary>
    /// Quem executou. <strong>Guardado para poder ser confrontado</strong>: BR-3
    /// exige que não seja nenhum dos que decidiram, e a verificação é feita no
    /// momento — mas o registo tem de ficar para quem conferir depois.
    /// </summary>
    public Guid? ExecutedByEmployeeId { get; private set; }

    public DateTimeOffset? ExecutedAt { get; private set; }

    public PaymentMethod? ExecutedMethod { get; private set; }

    public string? ExecutionReference { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    /// <param name="approvalRequestId">
    /// O processo já submetido. Obrigatório: **um pedido de pagamento nasce com
    /// governança ou não nasce** (BR-1). Criá-lo primeiro e submetê-lo depois
    /// deixaria uma janela em que existe um pedido pagável sem decisão.
    /// </param>
    public static PaymentRequest Create(
        PurchaseInvoice invoice,
        decimal amount,
        Guid requestedByEmployeeId,
        Guid approvalRequestId,
        DateOnly requestedOn,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.Status is InvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"A factura {invoice.SupplierInvoiceNumber} está anulada e não se paga.");
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Um pedido de pagamento é de valor maior que zero.");
        }

        if (amount > invoice.GrossTotal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount,
                $"O pedido é de {amount:N2} e a factura {invoice.SupplierInvoiceNumber} é de " +
                $"{invoice.GrossTotal:N2}.");
        }

        if (requestedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um pedido de pagamento tem sempre requisitante — é contra ele que BR-2 é verificada.",
                nameof(requestedByEmployeeId));
        }

        if (approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um pedido de pagamento nasce com processo de aprovação (BR-1).",
                nameof(approvalRequestId));
        }

        return new PaymentRequest(
            Guid.CreateVersion7(),
            invoice.Id,
            invoice.SupplierInvoiceNumber,
            invoice.Supplier,
            Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            invoice.Currency,
            requestedByEmployeeId,
            approvalRequestId,
            requestedOn,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    /// <summary>
    /// Marca o pedido como executado.
    ///
    /// <para>
    /// <strong>O que este método impõe é só o que o agregado consegue ver.</strong>
    /// A dupla barreira de BR-5 — decisão aprovada e saldo disponível — precisa
    /// de `approval` e da conta, que estão fora daqui: é a camada Application
    /// que a monta. O que fica cá é a parte indiscutível: não se executa duas
    /// vezes, não se executa um pedido cancelado, e quem executa não pode ser
    /// quem decidiu (BR-3).
    /// </para>
    /// </summary>
    /// <param name="deciders">
    /// Quem interveio na decisão, vindo de `approval`. Se o executor estiver
    /// nesta lista, é BR-3 a ser violada.
    /// </param>
    public void MarkExecuted(
        Guid accountId,
        Guid executedByEmployeeId,
        PaymentMethod method,
        IReadOnlyCollection<Guid> deciders,
        DateTimeOffset at,
        string? reference = null)
    {
        if (Status is PaymentRequestStatus.Executed)
        {
            throw new InvalidOperationException(
                $"O pedido já foi executado em {ExecutedAt:yyyy-MM-dd}. Pagar duas vezes é pagar a dobrar.");
        }

        if (Status is PaymentRequestStatus.Cancelled)
        {
            throw new InvalidOperationException("Um pedido cancelado não se executa.");
        }

        if (executedByEmployeeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Um pagamento regista sempre quem o executou.", nameof(executedByEmployeeId));
        }

        // BR-3. Excepção própria porque uma tentativa de a violar é evento de
        // segurança, não um erro de estado — mesma razão de
        // `SegregationOfDutiesException` em `approval`.
        if (deciders is not null && deciders.Contains(executedByEmployeeId))
        {
            throw new SegregationOfDutiesException(
                "Quem aprova não paga (BR-3): esta pessoa decidiu sobre este pedido.");
        }

        Status = PaymentRequestStatus.Executed;
        ExecutedFromAccountId = accountId;
        ExecutedByEmployeeId = executedByEmployeeId;
        ExecutedMethod = method;
        ExecutedAt = at;
        ExecutionReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
    }

    /// <summary>
    /// Cancela o pedido. Não elimina (BR-14). Um pedido já executado não se
    /// cancela — o dinheiro saiu, e desfazê-lo é outro movimento.
    /// </summary>
    public void Cancel(string reason, DateTimeOffset at)
    {
        if (Status is PaymentRequestStatus.Executed)
        {
            throw new InvalidOperationException(
                "Um pedido executado não se cancela — o dinheiro saiu. Desfazer é outro movimento.");
        }

        if (Status is PaymentRequestStatus.Cancelled)
        {
            throw new InvalidOperationException("O pedido já está cancelado.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancelar exige motivo.", nameof(reason));
        }

        Status = PaymentRequestStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = at;
    }
}

/// <summary>
/// Estados do pedido de pagamento.
///
/// <para>
/// <strong>Não há "pendente de aprovação".</strong> Esse é estado do processo em
/// `approval`, e copiá-lo para cá seria embutir o workflow que
/// `modules/finance.md` proíbe.
/// </para>
/// </summary>
public enum PaymentRequestStatus
{
    /// <summary>Criado e submetido. Pagável assim que a decisão o permita.</summary>
    Eligible,

    Executed,

    Cancelled,
}

/// <summary>
/// Tentativa de violar a segregação de funções (BR-3).
///
/// <para>
/// Distinta de um erro de estado qualquer: quem tenta pagar um pedido que
/// aprovou está a contornar um controlo, e isso vai para a trilha como evento
/// próprio — não como um <c>409</c> anónimo.
/// </para>
/// </summary>
public sealed class SegregationOfDutiesException(string message) : InvalidOperationException(message);
