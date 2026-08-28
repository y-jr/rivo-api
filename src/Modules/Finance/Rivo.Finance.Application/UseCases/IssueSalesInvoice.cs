using Microsoft.Extensions.Options;
using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Emite uma factura de venda.
///
/// <para>
/// É onde os três módulos se encontram: `commercial` dá o cliente, `fiscal` dá
/// a taxa à data do facto gerador, e `finance` possui o documento. Nenhum lê as
/// tabelas do outro — tudo passa por <see cref="ICustomerDirectory"/> e
/// <see cref="ITaxDetermination"/>.
/// </para>
/// </summary>
public sealed class IssueSalesInvoice(
    ISalesInvoiceStore store,
    ICustomerDirectory customers,
    ITaxDetermination taxes,
    IAuditTrail audit,
    PostDocument posting,
    TimeProvider clock,
    IOptions<FinanceOptions> options)
{
    private readonly FinanceOptions _options = options.Value;

    /// <param name="customerId">
    /// Nulo é uma venda a consumidor final — não é campo esquecido.
    /// </param>
    public async Task<IssueInvoiceResult> ExecuteAsync(
        Guid? customerId,
        string seriesCode,
        DateOnly issuedOn,
        DateOnly? taxPointDate,
        string currency,
        IReadOnlyList<InvoiceLineInput> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return IssueInvoiceResult.Rejected("Uma factura tem pelo menos uma linha.");
        }

        // O facto gerador é o que determina a taxa. Por omissão coincide com a
        // data do documento, que é o caso corrente.
        var facto = taxPointDate ?? issuedOn;

        // Duas origens para a mesma coisa: o retrato do cliente que vai ficar
        // congelado na factura.
        InvoicedParty parte;

        if (customerId is { } identificador)
        {
            var cliente = await customers.FindAsync(identificador, cancellationToken);

            if (cliente is null)
            {
                return IssueInvoiceResult.CustomerNotFound();
            }

            // Facturar um cliente desactivado é quase sempre engano — e o
            // cliente foi desactivado justamente para deixar de aparecer nestes
            // fluxos.
            if (cliente.Status is CustomerStatus.Inactive)
            {
                return IssueInvoiceResult.Rejected(
                    $"O cliente '{cliente.Name}' está desactivado. Reactive-o antes de facturar.");
            }

            parte = new InvoicedParty(
                cliente.Name,
                cliente.TaxId,
                cliente.BillingAddress.Detail,
                cliente.BillingAddress.City,
                cliente.BillingAddress.Country);
        }
        else
        {
            // Venda a quem não se identificou. O identificador vem de
            // configuração porque a convenção angolana não está verificada em
            // fonte primária — sem ele, recusa-se e diz-se porquê.
            if (string.IsNullOrWhiteSpace(_options.FinalConsumerTaxId))
            {
                return IssueInvoiceResult.Rejected(
                    "Facturar a consumidor final exige `Finance:FinalConsumerTaxId` configurado. " +
                    "A convenção angolana para esse identificador não está fixada neste sistema.");
            }

            parte = InvoicedParty.FinalConsumer(
                _options.FinalConsumerTaxId, _options.FinalConsumerName);
        }

        // As taxas resolvem-se todas antes de se tocar na série: se alguma
        // faltar, a factura não chega a nascer e nenhum número é queimado.
        var resolvidas = new List<NewInvoiceLine>(lines.Count);

        foreach (var linha in lines)
        {
            var determinacao = await taxes.DetermineAsync(
                new TaxDeterminationRequest(TaxKind.ValueAdded, linha.TaxCode, facto),
                cancellationToken);

            switch (determinacao.Outcome)
            {
                case TaxDeterminationOutcome.Determined:
                    resolvidas.Add(new NewInvoiceLine(
                        linha.Description,
                        linha.Quantity,
                        linha.UnitPrice,
                        determinacao.Determination!.TaxCode,
                        determinacao.Determination.Percentage));
                    break;

                case TaxDeterminationOutcome.NoRateInForce:
                    return IssueInvoiceResult.Rejected(
                        $"Não há taxa em vigor para o código '{linha.TaxCode}' a {facto:yyyy-MM-dd}. " +
                        "Configure a taxa em /fiscal/tax-rates antes de emitir.");

                case TaxDeterminationOutcome.ExemptionCodeUnavailable:
                    return IssueInvoiceResult.ExemptionUnavailable();

                default:
                    return IssueInvoiceResult.Rejected("Resultado inesperado na determinação fiscal.");
            }
        }

        // Série por omissão em configuração e não literal na Api: é a mesma que
        // o seed abre, e um literal duplicado divergiria dela em silêncio.
        var codigoSerie = string.IsNullOrWhiteSpace(seriesCode)
            ? _options.DefaultSeries
            : seriesCode;

        var serie = await store.FindSeriesForAllocationAsync(
            DocumentType.FT, (codigoSerie ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);

        if (serie is null)
        {
            return IssueInvoiceResult.SeriesNotFound();
        }

        SalesInvoice factura;

        try
        {
            // Atribuir o número é o primeiro acto irreversível. Tudo o que podia
            // falhar já falhou acima.
            var numero = serie.Allocate();

            factura = SalesInvoice.Issue(
                numero,
                issuedOn,
                facto,
                customerId,
                parte,
                currency,
                resolvidas,
                // Congelada na emissão. Vazia em configuração significa sistema
                // certificado, e as facturas anteriores mantêm a que lhes foi
                // gravada.
                _options.FiscalNotice);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return IssueInvoiceResult.Rejected(error.Message);
        }

        await store.AddAsync(factura, cancellationToken);

        // **A contabilidade entra na mesma transacção que o documento.** Se a
        // postagem falhar, a factura não é emitida — um documento emitido que
        // não lançou seria um buraco nos livros que ninguém vê.
        var lancamento = await posting.PostAsync(
            new DocumentPosting(
                PostingEvent.SalesInvoiceIssued,
                factura.Number.Formatted,

                // O número da factura é único por construção — a série garante-o.
                factura.Number.Formatted,
                $"Venda a {factura.Customer.Name}",
                factura.IssuedOn,
                factura.NetTotal,
                factura.TaxTotal,
                factura.GrossTotal,
                PostingSources.Automatic,
                clock.GetUtcNow()),
            cancellationToken);

        if (lancamento.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return IssueInvoiceResult.PostingBlocked(lancamento.Error!);
        }

        // Uma só gravação: o avanço da série, a factura e o lançamento entram
        // na mesma transacção. Se a série colidir com outra emissão simultânea,
        // nada é gravado — e a colisão sai como 409 (ADR-035), não como número
        // duplicado.
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.InvoiceIssued,
                FinanceAuditEntityTypes.SalesInvoice,
                factura.Id.ToString(),
                context,
                NewValue: $$"""
                    {"number":"{{factura.Number.Formatted}}","customerTaxId":"{{factura.Customer.TaxId}}","grossTotal":{{factura.GrossTotal}},"currency":"{{factura.Currency}}"}
                    """),
            cancellationToken);

        return IssueInvoiceResult.Success(factura.Id, factura.Number.Formatted);
    }
}

public sealed record InvoiceLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode);

public sealed record IssueInvoiceResult(
    IssueInvoiceOutcome Outcome,
    Guid? InvoiceId,
    string? Number,
    string? Error)
{
    public static IssueInvoiceResult Success(Guid invoiceId, string number) =>
        new(IssueInvoiceOutcome.Issued, invoiceId, number, null);

    /// <summary>
    /// Há regra de postagem e não se consegue honrar. **A factura não é
    /// emitida**: um documento que não lançou seria um buraco nos livros.
    /// </summary>
    public static IssueInvoiceResult PostingBlocked(string error) =>
        new(IssueInvoiceOutcome.PostingBlocked, null, null, error);

    public static IssueInvoiceResult Rejected(string error) =>
        new(IssueInvoiceOutcome.Rejected, null, null, error);

    public static IssueInvoiceResult CustomerNotFound() =>
        new(IssueInvoiceOutcome.CustomerNotFound, null, null, null);

    public static IssueInvoiceResult SeriesNotFound() =>
        new(IssueInvoiceOutcome.SeriesNotFound, null, null, null);

    public static IssueInvoiceResult ExemptionUnavailable() =>
        new(IssueInvoiceOutcome.ExemptionUnavailable, null, null, null);
}

public enum IssueInvoiceOutcome
{
    Issued,
    Rejected,
    CustomerNotFound,
    SeriesNotFound,

    /// <summary>
    /// Uma linha invoca isenção, e o catálogo de códigos de isenção não existe
    /// (ADR-036). Emitir assim produziria um documento inválido — não se
    /// inventa código.
    /// </summary>
    ExemptionUnavailable,

    /// <summary>
    /// A contabilidade automática está ligada e a postagem falhou — período
    /// fechado, conta em falta, diário morto. **409**: é o estado dos livros
    /// ou da configuração que impede, não o pedido.
    /// </summary>
    PostingBlocked,
}

/// <summary>Acções de `finance` na trilha de auditoria.</summary>
public static class FinanceAuditActions
{
    public const string InvoiceIssued = "finance.sales_invoice.issued";
    public const string InvoiceCancelled = "finance.sales_invoice.cancelled";
    public const string SeriesOpened = "finance.document_series.opened";

    public const string CreditNoteIssued = "finance.credit_note.issued";
    public const string CreditNoteCancelled = "finance.credit_note.cancelled";

    public const string ReceiptRegistered = "finance.receipt.registered";

    /// <summary>
    /// O estorno de um recebimento. Acção própria e não um cancelamento
    /// qualquer: a dívida volta a existir, e isso é facto com consequência.
    /// </summary>
    public const string ReceiptCancelled = "finance.receipt.cancelled";

    public const string AccountOpened = "finance.bank_account.opened";
    public const string AccountDeposited = "finance.bank_account.deposited";

    /// <summary>
    /// Saída de conta que não é pagamento a fornecedor — comissões, transferências
    /// entre contas. O pagamento propriamente dito tem a acção própria
    /// <see cref="PaymentExecuted"/>; esta é para o resto do que sai.
    /// </summary>
    public const string AccountWithdrawn = "finance.bank_account.withdrawn";

    public const string AccountClosed = "finance.bank_account.closed";
    public const string AccountReopened = "finance.bank_account.reopened";

    public const string PurchaseInvoiceRegistered = "finance.purchase_invoice.registered";

    public const string PaymentRequested = "finance.payment_request.created";
    public const string PaymentRequestCancelled = "finance.payment_request.cancelled";

    /// <summary>Dinheiro que saiu. É o registo mais sensível do módulo.</summary>
    public const string PaymentExecuted = "finance.payment_request.executed";

    /// <summary>
    /// Tentativa de pagar um pedido que a própria pessoa aprovou (BR-3).
    ///
    /// <para>
    /// Acção própria e não um erro qualquer: é evento de segurança, e uma
    /// sequência delas contra o mesmo pedido é o padrão que interessa detectar.
    /// </para>
    /// </summary>
    public const string PaymentSegregationRefused = "finance.payment_request.segregation_refused";

    // ---- Contabilidade ----

    public const string AccountOpenedInChart = "finance.ledger_account.opened";
    public const string JournalOpened = "finance.journal.opened";
    public const string EntryPosted = "finance.journal_entry.posted";
    public const string EntryVoided = "finance.journal_entry.voided";

    /// <summary>
    /// Fecho de período. É o acto que torna números definitivos.
    /// </summary>
    public const string PeriodClosed = "finance.accounting_period.closed";

    /// <summary>
    /// <strong>Reabertura de período fechado.</strong> Acção própria e das mais
    /// sensíveis do módulo: faz números já reportados voltarem a mexer-se, e
    /// quem audita precisa de a encontrar sem a procurar entre os fechos.
    /// </summary>
    public const string PeriodReopened = "finance.accounting_period.reopened";

    // ---- Planeamento ----

    public const string CostCentreOpened = "finance.cost_centre.opened";
    public const string BudgetDrafted = "finance.budget.drafted";
    public const string BudgetRevised = "finance.budget.revised";

    /// <summary>
    /// Aprovação de orçamento. É o momento em que o tecto passa a controlar, e
    /// por isso o momento a que uma auditoria de BR-8 volta.
    /// </summary>
    public const string BudgetApproved = "finance.budget.approved";

    public const string ForecastSubmitted = "finance.cost_forecast.submitted";

    // ---- Postagem automática ----

    public const string PostingRuleDefined = "finance.posting_rule.defined";
    public const string PostingRuleDeactivated = "finance.posting_rule.deactivated";
}

public static class FinanceAuditEntityTypes
{
    public const string SalesInvoice = "finance.sales_invoice";
    public const string DocumentSeries = "finance.document_series";
    public const string CreditNote = "finance.credit_note";
    public const string Receipt = "finance.receipt";
    public const string BankAccount = "finance.bank_account";
    public const string PurchaseInvoice = "finance.purchase_invoice";
    public const string PaymentRequest = "finance.payment_request";
    public const string LedgerAccount = "finance.ledger_account";
    public const string Journal = "finance.journal";
    public const string JournalEntry = "finance.journal_entry";
    public const string AccountingPeriod = "finance.accounting_period";
    public const string CostCentre = "finance.cost_centre";
    public const string Budget = "finance.budget";
    public const string CostForecast = "finance.cost_forecast";
    public const string PostingRule = "finance.posting_rule";
}
