using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Executa um pagamento. É o ponto de consistência forte do sistema.
///
/// <para>
/// <strong>A dupla barreira de BR-5 monta-se aqui</strong>, porque nenhuma das
/// metades cabe num agregado só: o estado da decisão vive em `approval`, e o
/// saldo vive na conta. O pedido de pagamento não vê nem um nem outro.
/// </para>
///
/// <para>
/// <strong>A decisão é revalidada no momento</strong>, não lida de um campo.
/// Entre a aprovação e a execução podem passar dias, e nesse intervalo o
/// processo pode ter sido cancelado — pagar com base numa aprovação lida na
/// semana passada é exactamente o que BR-5 existe para impedir.
/// </para>
///
/// <para>
/// É por este ponto que o ADR-001 escolheu monólito modular: as três escritas —
/// saldo, pedido e trilha — entram na mesma transacção, e uma falha a meio não
/// deixa dinheiro fora com o pedido por executar.
/// </para>
/// </summary>
public sealed class ExecutePayment(
    IPayablesStore store,
    IPaymentApproval approval,
    IAuditTrail audit,
    PostDocument posting,
    TimeProvider clock)
{
    public async Task<ExecutePaymentResult> ExecuteAsync(
        Guid paymentRequestId,
        Guid bankAccountId,
        Guid executedByEmployeeId,
        PaymentMethod method,
        string? reference,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindPaymentRequestForUpdateAsync(paymentRequestId, cancellationToken);

        if (pedido is null)
        {
            return ExecutePaymentResult.RequestNotFound();
        }

        // O estado do pedido é a primeira verificação, e a ordem importa para a
        // mensagem: sem isto, uma segunda tentativa sobre um pedido já executado
        // esbarrava primeiro no saldo e reportava "falta de dinheiro" quando a
        // razão real é "já foi pago". O agregado recusaria na mesma — mas quem
        // lê o erro ia procurar o problema no sítio errado.
        if (pedido.Status is not PaymentRequestStatus.Eligible)
        {
            return ExecutePaymentResult.NotExecutable(pedido.Status switch
            {
                PaymentRequestStatus.Executed =>
                    $"O pedido já foi executado em {pedido.ExecutedAt:yyyy-MM-dd}. " +
                    "Pagar duas vezes é pagar a dobrar.",
                _ => "Um pedido cancelado não se executa.",
            });
        }

        var conta = await store.FindAccountForUpdateAsync(bankAccountId, cancellationToken);

        if (conta is null)
        {
            return ExecutePaymentResult.AccountNotFound();
        }

        // Pagar em AOA de uma conta em USD esconderia o câmbio aplicado, e o
        // câmbio é uma decisão que ninguém tomou aqui.
        if (!string.Equals(conta.Currency, pedido.Currency, StringComparison.Ordinal))
        {
            return ExecutePaymentResult.Rejected(
                $"O pedido é em {pedido.Currency} e a conta é em {conta.Currency}. " +
                "Não há conversão automática — o câmbio é uma decisão, não um detalhe.");
        }

        // **Primeira barreira: a decisão, revalidada agora.**
        var estado = await approval.GetStateAsync(pedido.ApprovalRequestId, cancellationToken);

        if (estado.Status is not PaymentApprovalStatus.Approved)
        {
            // Unknown incluído de propósito: a ausência de decisão não é
            // aprovação, e por omissão não se paga (BR-1).
            return ExecutePaymentResult.NotApproved(estado.Status);
        }

        try
        {
            // **Segunda barreira: o saldo.** Sai antes de marcar o pedido para
            // que um saldo insuficiente não deixe o pedido executado sem
            // dinheiro ter saído.
            // O extracto ganha a linha no mesmo acto que o saldo, e com a
            // origem apontada: é por `payment_request` que a reconciliação
            // volta do movimento ao documento que o causou.
            conta.Withdraw(
                pedido.Amount,
                clock.GetUtcNow(),
                $"Pagamento a {pedido.Payee.Name}, factura {pedido.SupplierInvoiceNumber}",
                sourceType: BankMovementSources.PaymentRequest,
                sourceId: pedido.Id);

            // BR-3 é imposta pelo agregado, com a lista que veio de `approval`.
            pedido.MarkExecuted(
                conta.Id, executedByEmployeeId, method,
                estado.DecidedByEmployeeIds, clock.GetUtcNow(), reference);
        }
        catch (InsufficientFundsException error)
        {
            return ExecutePaymentResult.InsufficientFunds(error.Message);
        }
        catch (SegregationOfDutiesException error)
        {
            // Registada na trilha com acção própria: uma tentativa de contornar
            // BR-3 é evento de segurança, e uma sequência delas é o padrão que
            // interessa detectar.
            await audit.RecordAsync(
                new AuditRecord(
                    FinanceAuditActions.PaymentSegregationRefused,
                    FinanceAuditEntityTypes.PaymentRequest,
                    pedido.Id.ToString(),
                    context,
                    NewValue: $$"""{"attemptedBy":"{{executedByEmployeeId}}"}"""),
                cancellationToken);

            return ExecutePaymentResult.SegregationOfDuties(error.Message);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return ExecutePaymentResult.Rejected(error.Message);
        }

        // O pagamento também lança: sai dinheiro e a dívida ao fornecedor baixa.
        // Sem imposto a separar — o líquido é o próprio montante pago.
        var lancamento = await posting.PostAsync(
            new DocumentPosting(
                PostingEvent.PaymentExecuted,
                $"Pagamento de {pedido.SupplierInvoiceNumber}",

                // Um pagamento não tem número próprio, e **dois pagamentos
                // parciais da mesma factura no mesmo dia são legítimos** — a
                // chave tem de ser a do pedido, não a da factura.
                DocumentPosting.KeyFor("PG", pedido.Id),
                $"Pagamento a {pedido.Payee.Name}",
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                pedido.Amount,
                0m,
                pedido.Amount,
                PostingSources.Automatic,
                clock.GetUtcNow(),
                pedido.CostCentreId),
            cancellationToken);

        if (lancamento.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return ExecutePaymentResult.PostingBlocked(lancamento.Error!);
        }

        // Uma só gravação: saldo, pedido e lançamento na mesma transacção. Se o
        // contador de concorrência da conta colidir com outro pagamento
        // simultâneo, nada é gravado — e a colisão sai como 409 (ADR-035), não
        // como saldo negativo.
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentExecuted,
                FinanceAuditEntityTypes.PaymentRequest,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""
                    {"amount":{{pedido.Amount}},"currency":"{{pedido.Currency}}","account":"{{conta.Name}}","method":"{{method}}","executedBy":"{{executedByEmployeeId}}","approvalRequest":"{{pedido.ApprovalRequestId}}"}
                    """),
            cancellationToken);

        return ExecutePaymentResult.Success(conta.Balance);
    }
}

public sealed record ExecutePaymentResult(
    ExecutePaymentOutcome Outcome,
    decimal? RemainingBalance,
    string? Error)
{
    public static ExecutePaymentResult Success(decimal remaining) =>
        new(ExecutePaymentOutcome.Executed, remaining, null);

    public static ExecutePaymentResult RequestNotFound() =>
        new(ExecutePaymentOutcome.RequestNotFound, null, null);

    public static ExecutePaymentResult AccountNotFound() =>
        new(ExecutePaymentOutcome.AccountNotFound, null, null);

    public static ExecutePaymentResult NotApproved(PaymentApprovalStatus status) =>
        new(ExecutePaymentOutcome.NotApproved, null, status switch
        {
            PaymentApprovalStatus.Pending => "O processo de aprovação ainda está em curso.",
            PaymentApprovalStatus.Refused => "O processo de aprovação foi recusado ou cancelado.",
            _ => "Não há decisão de aprovação para este pedido. Sem decisão não se paga (BR-1).",
        });

    public static ExecutePaymentResult InsufficientFunds(string error) =>
        new(ExecutePaymentOutcome.InsufficientFunds, null, error);

    public static ExecutePaymentResult SegregationOfDuties(string error) =>
        new(ExecutePaymentOutcome.SegregationOfDuties, null, error);

    public static ExecutePaymentResult Rejected(string error) =>
        new(ExecutePaymentOutcome.Rejected, null, error);

    /// <summary>Postagem automática ligada e falhada. O dinheiro não sai.</summary>
    public static ExecutePaymentResult PostingBlocked(string error) =>
        new(ExecutePaymentOutcome.PostingBlocked, null, error);

    public static ExecutePaymentResult NotExecutable(string error) =>
        new(ExecutePaymentOutcome.NotExecutable, null, error);
}

public enum ExecutePaymentOutcome
{
    Executed,
    RequestNotFound,
    AccountNotFound,

    /// <summary>Sem decisão aprovada — BR-1. Traduz-se em 409.</summary>
    NotApproved,

    /// <summary>Sem saldo — a outra metade de BR-5. Traduz-se em 409.</summary>
    InsufficientFunds,

    /// <summary>
    /// Já executado, ou cancelado. Conflito de estado — 409, e com a razão
    /// certa em vez da do saldo.
    /// </summary>
    NotExecutable,

    /// <summary>
    /// Quem aprova não paga (BR-3). <strong>403 e não 409</strong>: não é o
    /// estado que impede, é *esta pessoa* — mesma distinção que `approval` faz.
    /// </summary>
    SegregationOfDuties,

    /// <summary>Contabilidade automática ligada e a postagem falhou — 409.</summary>
    PostingBlocked,

    Rejected,
}
