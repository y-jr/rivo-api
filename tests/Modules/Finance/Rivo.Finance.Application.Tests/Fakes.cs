using Microsoft.Extensions.Options;
using Rivo.Audit.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;
using Rivo.Procurement.Contracts;

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

    public Task<decimal> SumOutstandingAsync(string currency, CancellationToken cancellationToken)
    {
        var facturado = _invoices.Values
            .Where(i => i.Status is not InvoiceStatus.Cancelled && i.Currency == currency)
            .Sum(i => i.GrossTotal);

        var creditado = _creditNotes
            .Where(n => n.Status is not InvoiceStatus.Cancelled && n.Currency == currency)
            .Sum(n => n.GrossTotal);

        var recebido = _receipts
            .Where(r => r.Status is not InvoiceStatus.Cancelled && r.Currency == currency)
            .SelectMany(r => r.Lines)
            .Sum(s => s.Amount);

        return Task.FromResult(facturado - creditado - recebido);
    }

    public Task<decimal> SumNetInvoicedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.Values
            .Where(i => i.Status is not InvoiceStatus.Cancelled
                && i.Currency == currency
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .Sum(i => i.NetTotal));

    public Task<decimal> SumNetCreditedAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_creditNotes
            .Where(n => n.Status is not InvoiceStatus.Cancelled
                && n.Currency == currency
                && n.IssuedOn >= from
                && n.IssuedOn <= to)
            .Sum(n => n.NetTotal));

    public Task<IReadOnlyList<CustomerInvoicedTotal>> TopCustomersByInvoicedAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken)
    {
        var topo = _invoices.Values
            .Where(i => i.Status is not InvoiceStatus.Cancelled
                && i.Currency == currency
                && i.CustomerId is not null
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .GroupBy(i => i.CustomerId!.Value)
            .Select(g => new CustomerInvoicedTotal(g.Key, g.Sum(i => i.NetTotal)))
            .OrderByDescending(c => c.NetTotal)
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<CustomerInvoicedTotal>>(topo);
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

    public Task<decimal> SumNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.Values
            .Where(i => i.Status is not InvoiceStatus.Cancelled
                && i.Currency == currency
                && i.IssuedOn >= from
                && i.IssuedOn <= to)
            .Sum(i => i.NetTotal));

    public Task<decimal> SumOutstandingPayablesAsync(string currency, CancellationToken cancellationToken)
    {
        var facturado = _invoices.Values
            .Where(i => i.Status is not InvoiceStatus.Cancelled && i.Currency == currency)
            .Sum(i => i.GrossTotal);

        var pago = _requests.Values
            .Where(r => r.Status is PaymentRequestStatus.Executed && r.Currency == currency)
            .Sum(r => r.Amount);

        return Task.FromResult(facturado - pago);
    }

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

internal sealed class FakePlanningStore : IPlanningStore
{
    private readonly Dictionary<Guid, CostCentre> _centres = [];
    private readonly List<Budget> _budgets = [];
    private readonly List<DepartmentCostForecast> _forecasts = [];

    /// <summary>
    /// Comprometido por centro de custo e mês, posto à mão. Aqui não há pedidos
    /// de pagamento a somar — o que se testa é a decisão de BR-8, não a soma.
    /// </summary>
    private readonly Dictionary<(Guid, int, int), decimal> _committed = [];

    public int SaveCount { get; private set; }

    public FakePlanningStore With(CostCentre centre)
    {
        _centres[centre.Id] = centre;
        return this;
    }

    public FakePlanningStore With(Budget budget)
    {
        _budgets.Add(budget);
        return this;
    }

    public FakePlanningStore WithCommitted(Guid costCentreId, int year, int month, decimal amount)
    {
        _committed[(costCentreId, year, month)] = amount;
        return this;
    }

    public Task<CostCentre?> FindCostCentreAsync(Guid costCentreId, CancellationToken cancellationToken) =>
        Task.FromResult(_centres.GetValueOrDefault(costCentreId));

    public Task<CostCentre?> FindCostCentreForUpdateAsync(Guid costCentreId, CancellationToken cancellationToken) =>
        FindCostCentreAsync(costCentreId, cancellationToken);

    public Task<bool> CostCentreCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_centres.Values.Any(c => c.Code == code));

    public Task<IReadOnlyList<CostCentre>> ListCostCentresForDepartmentAsync(
        Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCentre>>(
            [.. _centres.Values.Where(c => c.DepartmentId == departmentId)]);

    public Task<IReadOnlyList<CostCentre>> ListCostCentresAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCentre>>(
            [.. _centres.Values.Where(c => includeInactive || c.IsActive)]);

    public Task AddCostCentreAsync(CostCentre costCentre, CancellationToken cancellationToken)
    {
        _centres[costCentre.Id] = costCentre;
        return Task.CompletedTask;
    }

    public Task<Budget?> FindBudgetAsync(Guid budgetId, CancellationToken cancellationToken) =>
        Task.FromResult(_budgets.FirstOrDefault(b => b.Id == budgetId));

    public Task<Budget?> FindBudgetForUpdateAsync(Guid budgetId, CancellationToken cancellationToken) =>
        FindBudgetAsync(budgetId, cancellationToken);

    public Task<Budget?> FindBudgetForAsync(
        Guid costCentreId, int fiscalYear, CancellationToken cancellationToken) =>
        Task.FromResult(_budgets.FirstOrDefault(
            b => b.CostCentreId == costCentreId && b.FiscalYear == fiscalYear));

    public Task<IReadOnlyList<Budget>> ListBudgetsAsync(
        Guid? costCentreId, int? fiscalYear, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Budget>>([.. _budgets]);

    public Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken)
    {
        _budgets.Add(budget);
        return Task.CompletedTask;
    }

    public Task<decimal> CommittedAgainstAsync(
        Guid costCentreId, int fiscalYear, int month, CancellationToken cancellationToken) =>
        Task.FromResult(_committed.GetValueOrDefault((costCentreId, fiscalYear, month)));

    public Task<DepartmentCostForecast?> FindForecastAsync(Guid forecastId, CancellationToken cancellationToken) =>
        Task.FromResult(_forecasts.FirstOrDefault(f => f.Id == forecastId));

    public Task<DepartmentCostForecast?> FindForecastForUpdateAsync(
        Guid forecastId, CancellationToken cancellationToken) =>
        FindForecastAsync(forecastId, cancellationToken);

    public Task<bool> ForecastExistsAsync(
        Guid departmentId, int fiscalYear, int month, CancellationToken cancellationToken) =>
        Task.FromResult(_forecasts.Any(
            f => f.DepartmentId == departmentId && f.FiscalYear == fiscalYear && f.Month == month));

    public Task<IReadOnlyList<DepartmentCostForecast>> ListForecastsAsync(
        Guid? departmentId, int? fiscalYear, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DepartmentCostForecast>>([.. _forecasts]);

    public Task AddForecastAsync(DepartmentCostForecast forecast, CancellationToken cancellationToken)
    {
        _forecasts.Add(forecast);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    private readonly Dictionary<Guid, CustomerReference> _customers = [];

    public FakeCustomerDirectory(CustomerReference? customer = null)
    {
        if (customer is not null)
        {
            _customers[customer.CustomerId] = customer;
        }
    }

    /// <summary>Para os testes com mais de um cliente — o único construtor não chega.</summary>
    public FakeCustomerDirectory With(CustomerReference customer)
    {
        _customers[customer.CustomerId] = customer;
        return this;
    }

    public Task<CustomerReference?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.GetValueOrDefault(customerId));
}

internal sealed class FakeSupplierDirectory(SupplierReference? supplier = null) : ISupplierDirectory
{
    public Task<SupplierReference?> FindAsync(Guid supplierId, CancellationToken cancellationToken) =>
        Task.FromResult(supplier is not null && supplier.SupplierId == supplierId ? supplier : null);

    public Task<SupplierReference?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken) =>
        Task.FromResult(
            supplier is not null && string.Equals(supplier.TaxId, taxId, StringComparison.OrdinalIgnoreCase)
                ? supplier
                : null);
}

internal sealed class FakePurchaseOrderDirectory(PurchaseOrderReference? order = null) : IPurchaseOrderDirectory
{
    public Task<PurchaseOrderReference?> FindAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(order is not null && order.PurchaseOrderId == purchaseOrderId ? order : null);
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
        Guid? departmentId, string? budgetReference, string summary,
        CancellationToken cancellationToken) =>
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

internal sealed class FakeLedgerStore : ILedgerStore
{
    private readonly Dictionary<Guid, LedgerAccount> _accounts = [];
    private readonly Dictionary<Guid, Journal> _journals = [];
    private readonly List<JournalEntry> _entries = [];
    private readonly List<AccountingPeriod> _periods = [];
    private readonly List<PostingRule> _rules = [];

    public int SaveCount { get; private set; }

    public FakeLedgerStore With(LedgerAccount account)
    {
        _accounts[account.Id] = account;
        return this;
    }

    public FakeLedgerStore With(Journal journal)
    {
        _journals[journal.Id] = journal;
        return this;
    }

    public FakeLedgerStore With(AccountingPeriod period)
    {
        _periods.Add(period);
        return this;
    }

    public FakeLedgerStore With(JournalEntry entry)
    {
        _entries.Add(entry);
        return this;
    }

    public Task<LedgerAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.GetValueOrDefault(accountId));

    public Task<LedgerAccount?> FindAccountByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.Values.FirstOrDefault(a => a.Code == code));

    public Task<LedgerAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken) =>
        FindAccountAsync(accountId, cancellationToken);

    public Task<IReadOnlyDictionary<string, LedgerAccount>> FindAccountsByCodeAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, LedgerAccount>>(
            _accounts.Values
                .Where(a => codes.Contains(a.Code))
                .ToDictionary(a => a.Code, StringComparer.Ordinal));

    public Task<IReadOnlyList<LedgerAccount>> ListAccountsAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LedgerAccount>>(
            [.. _accounts.Values.Where(a => includeInactive || a.IsActive).OrderBy(a => a.Code, StringComparer.Ordinal)]);

    public Task<bool> HasChildrenAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.Values.Any(a => a.ParentId == accountId && a.IsActive));

    public Task<bool> HasPostingsAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.SelectMany(e => e.Lines).Any(l => l.AccountId == accountId));

    public Task AddAccountAsync(LedgerAccount account, CancellationToken cancellationToken)
    {
        _accounts[account.Id] = account;
        return Task.CompletedTask;
    }

    public Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken) =>
        Task.FromResult(_journals.GetValueOrDefault(journalId));

    public Task<Journal?> FindJournalByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_journals.Values.FirstOrDefault(j => j.Code == code));

    public Task<IReadOnlyList<Journal>> ListJournalsAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Journal>>(
            [.. _journals.Values.Where(j => includeInactive || j.IsActive)]);

    public Task AddJournalAsync(Journal journal, CancellationToken cancellationToken)
    {
        _journals[journal.Id] = journal;
        return Task.CompletedTask;
    }

    public Task<JournalEntry?> FindEntryAsync(Guid entryId, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.Id == entryId));

    public Task<JournalEntry?> FindEntryForUpdateAsync(Guid entryId, CancellationToken cancellationToken) =>
        FindEntryAsync(entryId, cancellationToken);

    public Task<JournalEntry?> FindEntryByArchivalNumberAsync(
        string archivalNumber, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.ArchivalNumber == archivalNumber));

    public Task<IReadOnlyList<JournalEntry>> ListEntriesAsync(
        Guid? journalId, int? fiscalYear, int? period, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JournalEntry>>([.. _entries]);

    public Task<bool> EntryExistsAsync(
        DateOnly transactionDate, string journalCode, string archivalNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult(_entries.Any(
            e => e.TransactionDate == transactionDate
                && e.JournalCode == journalCode
                && e.ArchivalNumber == archivalNumber));

    public Task AddEntryAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<AccountingPeriod?> FindPeriodAsync(
        int fiscalYear, int number, CancellationToken cancellationToken) =>
        Task.FromResult(_periods.FirstOrDefault(p => p.FiscalYear == fiscalYear && p.Number == number));

    public Task<AccountingPeriod?> FindPeriodForUpdateAsync(
        int fiscalYear, int number, CancellationToken cancellationToken) =>
        FindPeriodAsync(fiscalYear, number, cancellationToken);

    public Task<IReadOnlyList<AccountingPeriod>> ListPeriodsAsync(
        int fiscalYear, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountingPeriod>>(
            [.. _periods.Where(p => p.FiscalYear == fiscalYear).OrderBy(p => p.Number)]);

    public Task AddPeriodAsync(AccountingPeriod period, CancellationToken cancellationToken)
    {
        _periods.Add(period);
        return Task.CompletedTask;
    }

    public FakeLedgerStore With(PostingRule rule)
    {
        _rules.Add(rule);
        return this;
    }

    public Task<PostingRule?> FindActivePostingRuleAsync(
        PostingEvent postingEvent, CancellationToken cancellationToken) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Event == postingEvent && r.IsActive));

    public Task<PostingRule?> FindPostingRuleAsync(Guid ruleId, CancellationToken cancellationToken) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Id == ruleId));

    public Task<PostingRule?> FindPostingRuleForUpdateAsync(
        Guid ruleId, CancellationToken cancellationToken) =>
        FindPostingRuleAsync(ruleId, cancellationToken);

    public Task<IReadOnlyList<PostingRule>> ListPostingRulesAsync(
        bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PostingRule>>(
            [.. _rules.Where(r => includeInactive || r.IsActive)]);

    public Task AddPostingRuleAsync(PostingRule rule, CancellationToken cancellationToken)
    {
        _rules.Add(rule);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A mesma conta que a implementação real: lançamentos **não anulados**, do
    /// ano, até ao período pedido.
    /// </summary>
    public Task<IReadOnlyList<AccountMovement>> AccountMovementsAsync(
        int fiscalYear, int? uptoPeriod, CancellationToken cancellationToken)
    {
        var linhas = _entries
            .Where(e => !e.IsVoided
                && e.TransactionDate.Year == fiscalYear
                && (uptoPeriod is not { } ate || e.Period <= ate))
            .SelectMany(e => e.Lines);

        return Task.FromResult<IReadOnlyList<AccountMovement>>(
            [.. linhas
                .GroupBy(l => (l.AccountId, l.AccountCode))
                .Select(g => new AccountMovement(
                    g.Key.AccountId,
                    g.Key.AccountCode,
                    g.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount),
                    g.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount)))]);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
