using Microsoft.Extensions.Options;
using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.Tests;

/// <summary>
/// Duplos escritos à mão, sem biblioteca de mocks — ADR-022 rejeitou
/// dependências que não resolvem problema nenhum.
///
/// <para>
/// Os stores guardam em memória em vez de devolverem valores fixos, porque
/// metade do que há para testar aqui são <strong>invariantes sobre o
/// conjunto</strong>: quanto já foi recebido de uma factura, quanto já está
/// pedido sobre uma compra. Um duplo que devolvesse um número fixo estaria a
/// testar o duplo.
/// </para>
/// </summary>
internal sealed class FakeSalesInvoiceStore : ISalesInvoiceStore
{
    private readonly Dictionary<Guid, SalesInvoice> _invoices = [];
    private readonly List<CreditNote> _creditNotes = [];
    private readonly List<Receipt> _receipts = [];
    private readonly Dictionary<(DocumentType, string), DocumentSeries> _series = [];

    public int SaveCount { get; private set; }

    public FakeSalesInvoiceStore WithSeries(params DocumentType[] types)
    {
        foreach (var type in types)
        {
            _series[(type, "S001")] = DocumentSeries.Open(type, "S001");
        }

        return this;
    }

    public FakeSalesInvoiceStore With(SalesInvoice invoice)
    {
        _invoices[invoice.Id] = invoice;
        return this;
    }

    public FakeSalesInvoiceStore With(CreditNote note)
    {
        _creditNotes.Add(note);
        return this;
    }

    public FakeSalesInvoiceStore With(Receipt receipt)
    {
        _receipts.Add(receipt);
        return this;
    }

    /// <summary>
    /// O próximo número da série. Serve para verificar que uma emissão recusada
    /// não queimou número nenhum.
    /// </summary>
    public int NextSequenceOf(DocumentType type) =>
        _series.TryGetValue((type, "S001"), out var serie) ? serie.NextSequence : 0;

    public Task<DocumentSeries?> FindSeriesForAllocationAsync(
        DocumentType type, string code, CancellationToken cancellationToken) =>
        Task.FromResult(_series.GetValueOrDefault((type, code)));

    public Task<IReadOnlyList<DocumentSeries>> ListSeriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentSeries>>([.. _series.Values]);

    public Task AddSeriesAsync(DocumentSeries series, CancellationToken cancellationToken)
    {
        _series[(series.Type, series.Code)] = series;
        return Task.CompletedTask;
    }

    public Task<bool> SeriesExistsAsync(DocumentType type, string code, CancellationToken cancellationToken) =>
        Task.FromResult(_series.ContainsKey((type, code)));

    public Task<SalesInvoice?> FindAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(invoiceId));

    public Task<SalesInvoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(invoiceId));

    public Task<IReadOnlyList<SalesInvoice>> ListAsync(
        Guid? customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SalesInvoice>>([.. _invoices.Values]);

    public Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    /// <summary>
    /// A mesma conta que a implementação real faz: total, menos creditado,
    /// menos recebido, contando só documentos não anulados.
    /// </summary>
    public Task<decimal> OutstandingAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!_invoices.TryGetValue(invoiceId, out var factura))
        {
            return Task.FromResult(0m);
        }

        var creditado = _creditNotes
            .Where(n => n.SalesInvoiceId == invoiceId && n.Status is not InvoiceStatus.Cancelled)
            .Sum(n => n.GrossTotal);

        var recebido = _receipts
            .Where(r => r.Status is not InvoiceStatus.Cancelled)
            .SelectMany(r => r.Lines)
            .Where(s => s.SalesInvoiceId == invoiceId)
            .Sum(s => s.Amount);

        return Task.FromResult(factura.GrossTotal - creditado - recebido);
    }

    public Task<CreditNote?> FindCreditNoteAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        Task.FromResult(_creditNotes.FirstOrDefault(n => n.Id == creditNoteId));

    public Task<CreditNote?> FindCreditNoteForUpdateAsync(Guid creditNoteId, CancellationToken cancellationToken) =>
        FindCreditNoteAsync(creditNoteId, cancellationToken);

    public Task<IReadOnlyList<CreditNote>> ListCreditNotesAsync(
        Guid? salesInvoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CreditNote>>([.. _creditNotes]);

    public Task AddCreditNoteAsync(CreditNote note, CancellationToken cancellationToken)
    {
        _creditNotes.Add(note);
        return Task.CompletedTask;
    }

    public Task<Receipt?> FindReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
        Task.FromResult(_receipts.FirstOrDefault(r => r.Id == receiptId));

    public Task<Receipt?> FindReceiptForUpdateAsync(Guid receiptId, CancellationToken cancellationToken) =>
        FindReceiptAsync(receiptId, cancellationToken);

    public Task<IReadOnlyList<Receipt>> ListReceiptsAsync(
        Guid? customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Receipt>>([.. _receipts]);

    public Task AddReceiptAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        _receipts.Add(receipt);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakePayablesStore : IPayablesStore
{
    private readonly Dictionary<Guid, BankAccount> _accounts = [];
    private readonly Dictionary<Guid, PurchaseInvoice> _invoices = [];
    private readonly Dictionary<Guid, PaymentRequest> _requests = [];

    public int SaveCount { get; private set; }

    public FakePayablesStore With(BankAccount account)
    {
        _accounts[account.Id] = account;
        return this;
    }

    public FakePayablesStore With(PurchaseInvoice invoice)
    {
        _invoices[invoice.Id] = invoice;
        return this;
    }

    public FakePayablesStore With(PaymentRequest request)
    {
        _requests[request.Id] = request;
        return this;
    }

    public Task<BankAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.GetValueOrDefault(accountId));

    public Task<BankAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.GetValueOrDefault(accountId));

    public Task<IReadOnlyList<BankAccount>> ListAccountsAsync(
        bool includeClosed, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BankAccount>>(
            [.. _accounts.Values.Where(a => includeClosed || a.IsActive)]);

    public Task AddAccountAsync(BankAccount account, CancellationToken cancellationToken)
    {
        _accounts[account.Id] = account;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BankMovement>> ListMovementsAsync(
        Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        IEnumerable<BankMovement> movimentos = _accounts.TryGetValue(accountId, out var conta)
            ? conta.Movements
            : [];

        if (from is { } inicio)
        {
            movimentos = movimentos.Where(m => DateOnly.FromDateTime(m.OccurredAt.UtcDateTime) >= inicio);
        }

        if (to is { } fim)
        {
            movimentos = movimentos.Where(m => DateOnly.FromDateTime(m.OccurredAt.UtcDateTime) <= fim);
        }

        return Task.FromResult<IReadOnlyList<BankMovement>>(
            [.. movimentos.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)]);
    }

    public Task<decimal> OpeningBalanceAsync(
        Guid accountId, DateOnly? from, CancellationToken cancellationToken)
    {
        if (from is not { } inicio || !_accounts.TryGetValue(accountId, out var conta))
        {
            return Task.FromResult(0m);
        }

        var anterior = conta.Movements
            .Where(m => DateOnly.FromDateTime(m.OccurredAt.UtcDateTime) < inicio)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .LastOrDefault();

        return Task.FromResult(anterior?.BalanceAfter ?? 0m);
    }

    public Task<PurchaseInvoice?> FindPurchaseInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(invoiceId));

    public Task<PurchaseInvoice?> FindPurchaseInvoiceForUpdateAsync(
        Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(invoiceId));

    public Task<IReadOnlyList<PurchaseInvoice>> ListPurchaseInvoicesAsync(
        DateOnly? dueBefore, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PurchaseInvoice>>([.. _invoices.Values]);

    public Task<bool> PurchaseInvoiceExistsAsync(
        string supplierTaxId, string supplierInvoiceNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.Values.Any(i =>
            i.SupplierTaxId == supplierTaxId && i.SupplierInvoiceNumber == supplierInvoiceNumber));

    public Task AddPurchaseInvoiceAsync(PurchaseInvoice invoice, CancellationToken cancellationToken)
    {
        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task<PaymentRequest?> FindPaymentRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.GetValueOrDefault(requestId));

    public Task<PaymentRequest?> FindPaymentRequestForUpdateAsync(
        Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.GetValueOrDefault(requestId));

    public Task<IReadOnlyList<PaymentRequest>> ListPaymentRequestsAsync(
        Guid? purchaseInvoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PaymentRequest>>([.. _requests.Values]);

    public Task<decimal> CommittedAsync(Guid purchaseInvoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.Values
            .Where(r => r.PurchaseInvoiceId == purchaseInvoiceId
                && r.Status is not PaymentRequestStatus.Cancelled)
            .Sum(r => r.Amount));

    public Task AddPaymentRequestAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        _requests[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCustomerDirectory(CustomerReference? customer = null) : ICustomerDirectory
{
    public Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(customer is not null && customer.CustomerId == customerId ? customer : null);
}

/// <summary>
/// Determinação fiscal com resposta fixa — mas <strong>guarda a data por que
/// foi perguntada</strong>. É essa data que o ADR-011 §3 fixa, e o único modo
/// de a verificar é observar o que a Application perguntou.
/// </summary>
internal sealed class FakeTaxDetermination(TaxDeterminationResult? result = null) : ITaxDetermination
{
    private readonly TaxDeterminationResult _result =
        result ?? TaxDeterminationResult.Determined(new TaxDetermination("NOR", 14m, "Lei 7/19"));

    public List<DateOnly> AskedFor { get; } = [];

    public Task<TaxDeterminationResult> DetermineAsync(
        TaxDeterminationRequest request, CancellationToken cancellationToken)
    {
        AskedFor.Add(request.TaxPointDate);
        return Task.FromResult(_result);
    }
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Records { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed class FakePaymentApproval : IPaymentApproval
{
    private readonly PaymentApprovalState _state;
    private readonly PaymentApprovalSubmissionResult _submission;

    public FakePaymentApproval(
        PaymentApprovalStatus status = PaymentApprovalStatus.Approved,
        IReadOnlyList<Guid>? deciders = null,
        bool available = true,
        PaymentApprovalSubmissionResult? submission = null)
    {
        _state = new PaymentApprovalState(status, deciders ?? []);
        IsAvailable = available;
        _submission = submission ?? PaymentApprovalSubmissionResult.Success(Guid.CreateVersion7());
    }

    public bool IsAvailable { get; }

    /// <summary>
    /// Quantas vezes a decisão foi relida. BR-5 exige revalidação no momento da
    /// execução — se este contador ficar a zero, a regra não está a acontecer.
    /// </summary>
    public int StateReads { get; private set; }

    public Task<PaymentApprovalSubmissionResult> SubmitAsync(
        Guid paymentRequestId, Guid requestedByEmployeeId, decimal amount, string currency,
        Guid? departmentId, string summary, CancellationToken cancellationToken) =>
        Task.FromResult(_submission);

    public Task<PaymentApprovalState> GetStateAsync(
        Guid approvalRequestId, CancellationToken cancellationToken)
    {
        StateReads++;
        return Task.FromResult(_state);
    }
}

/// <summary>
/// Relógio parado, para que o instante gravado seja verificável.
///
/// <para>
/// Escrito à mão em vez de trazer <c>Microsoft.Extensions.TimeProvider.Testing</c>:
/// o que se precisa daqui é uma leitura fixa, e um pacote inteiro para isso é
/// dependência que não resolve problema nenhum (ADR-022).
/// </para>
/// </summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

internal static class Opcoes
{
    public static IOptions<FinanceOptions> Financeiras(
        string finalConsumerTaxId = "CONSUMIDORFINAL",
        string fiscalNotice = "Documento sem validade fiscal.") =>
        Options.Create(new FinanceOptions
        {
            DefaultSeries = "S001",
            FinalConsumerTaxId = finalConsumerTaxId,
            FinalConsumerName = "Consumidor final",
            FiscalNotice = fiscalNotice,
        });
}
