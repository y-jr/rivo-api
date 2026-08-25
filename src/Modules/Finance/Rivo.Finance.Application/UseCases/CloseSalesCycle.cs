using Microsoft.Extensions.Options;
using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Emite uma nota de crédito sobre uma factura.
///
/// <para>
/// A taxa vem de `fiscal` <strong>à data do facto gerador da factura
/// corrigida</strong>, não à de hoje: o imposto que se devolve é o que foi
/// liquidado (ADR-011 §3).
/// </para>
/// </summary>
public sealed class IssueCreditNote(
    ISalesInvoiceStore store,
    ITaxDetermination taxes,
    IAuditTrail audit,
    PostDocument posting,
    TimeProvider clock,
    IOptions<FinanceOptions> options)
{
    private readonly FinanceOptions _options = options.Value;

    public async Task<IssueCreditNoteResult> ExecuteAsync(
        Guid salesInvoiceId,
        string seriesCode,
        DateOnly issuedOn,
        string reason,
        IReadOnlyList<InvoiceLineInput> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            return IssueCreditNoteResult.Rejected("Uma nota de crédito tem pelo menos uma linha.");
        }

        var factura = await store.FindForUpdateAsync(salesInvoiceId, cancellationToken);

        if (factura is null)
        {
            return IssueCreditNoteResult.InvoiceNotFound();
        }

        // Todas as taxas antes de tocar na série: se alguma faltar, nenhum
        // número é queimado.
        var resolvidas = new List<NewInvoiceLine>(lines.Count);

        foreach (var linha in lines)
        {
            var determinacao = await taxes.DetermineAsync(
                new TaxDeterminationRequest(TaxKind.ValueAdded, linha.TaxCode, factura.TaxPointDate),
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
                    return IssueCreditNoteResult.Rejected(
                        $"Não há taxa em vigor para o código '{linha.TaxCode}' a " +
                        $"{factura.TaxPointDate:yyyy-MM-dd}, que é o facto gerador da factura corrigida.");

                case TaxDeterminationOutcome.ExemptionCodeUnavailable:
                    return IssueCreditNoteResult.ExemptionUnavailable();

                default:
                    return IssueCreditNoteResult.Rejected("Resultado inesperado na determinação fiscal.");
            }
        }

        // **A invariante que nenhum agregado vê.** Creditar mais do que está em
        // aberto poria a factura com saldo negativo — dívida ao contrário, que
        // não é o que uma nota de crédito significa. Devolver dinheiro a mais é
        // um pagamento, e esse não se faz por aqui.
        var emAberto = await store.OutstandingAsync(factura.Id, cancellationToken);

        var bruto = resolvidas.Sum(linha =>
        {
            var liquido = Math.Round(linha.Quantity * linha.UnitPrice, 2, MidpointRounding.AwayFromZero);
            return liquido + Math.Round(liquido * linha.TaxPercentage / 100m, 2, MidpointRounding.AwayFromZero);
        });

        if (bruto > emAberto)
        {
            return IssueCreditNoteResult.ExceedsOutstanding(
                $"A nota é de {bruto:N2} {factura.Currency} e a factura " +
                $"{factura.Number.Formatted} só tem {emAberto:N2} em aberto.");
        }

        var serie = await store.FindSeriesForAllocationAsync(
            DocumentType.NC,
            NormalizeSeries(seriesCode, _options.DefaultSeries),
            cancellationToken);

        if (serie is null)
        {
            return IssueCreditNoteResult.SeriesNotFound();
        }

        CreditNote nota;

        try
        {
            nota = CreditNote.Issue(
                serie.Allocate(), factura, issuedOn, reason, resolvidas, _options.FiscalNotice);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return IssueCreditNoteResult.Rejected(error.Message);
        }

        await store.AddCreditNoteAsync(nota, cancellationToken);

        var lancamento = await posting.PostAsync(
            new DocumentPosting(
                PostingEvent.CreditNoteIssued,
                nota.Number.Formatted,
                nota.Number.Formatted,
                $"Correcção de {nota.CorrectedInvoiceNumber}",
                nota.IssuedOn,
                nota.NetTotal,
                nota.TaxTotal,
                nota.GrossTotal,
                PostingSources.Automatic,
                clock.GetUtcNow()),
            cancellationToken);

        if (lancamento.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return IssueCreditNoteResult.PostingBlocked(lancamento.Error!);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.CreditNoteIssued,
                FinanceAuditEntityTypes.CreditNote,
                nota.Id.ToString(),
                context,
                NewValue: $$"""
                    {"number":"{{nota.Number.Formatted}}","corrects":"{{nota.CorrectedInvoiceNumber}}","grossTotal":{{nota.GrossTotal}},"reason":"{{nota.Reason}}"}
                    """),
            cancellationToken);

        return IssueCreditNoteResult.Success(nota.Id, nota.Number.Formatted);
    }

    internal static string NormalizeSeries(string? requested, string? fallback) =>
        (string.IsNullOrWhiteSpace(requested) ? fallback ?? string.Empty : requested)
            .Trim().ToUpperInvariant();
}

public sealed record IssueCreditNoteResult(
    IssueCreditNoteOutcome Outcome,
    Guid? CreditNoteId,
    string? Number,
    string? Error)
{
    public static IssueCreditNoteResult Success(Guid id, string number) =>
        new(IssueCreditNoteOutcome.Issued, id, number, null);

    public static IssueCreditNoteResult Rejected(string error) =>
        new(IssueCreditNoteOutcome.Rejected, null, null, error);

    public static IssueCreditNoteResult InvoiceNotFound() =>
        new(IssueCreditNoteOutcome.InvoiceNotFound, null, null, null);

    public static IssueCreditNoteResult SeriesNotFound() =>
        new(IssueCreditNoteOutcome.SeriesNotFound, null, null, null);

    public static IssueCreditNoteResult ExemptionUnavailable() =>
        new(IssueCreditNoteOutcome.ExemptionUnavailable, null, null, null);

    /// <summary>Postagem automática ligada e falhada. A nota não é emitida.</summary>
    public static IssueCreditNoteResult PostingBlocked(string error) =>
        new(IssueCreditNoteOutcome.PostingBlocked, null, null, error);

    public static IssueCreditNoteResult ExceedsOutstanding(string error) =>
        new(IssueCreditNoteOutcome.ExceedsOutstanding, null, null, error);
}

public enum IssueCreditNoteOutcome
{
    Issued,
    Rejected,
    InvoiceNotFound,
    SeriesNotFound,
    ExemptionUnavailable,

    /// <summary>
    /// Credita mais do que está em aberto. Conflito de estado, não campo mal
    /// preenchido — 409.
    /// </summary>
    ExceedsOutstanding,

    /// <summary>Contabilidade automática ligada e a postagem falhou — 409.</summary>
    PostingBlocked,
}

/// <summary>
/// Regista dinheiro recebido contra facturas.
///
/// <para>
/// Todas as facturas do recibo têm de ser do <strong>mesmo cliente e da mesma
/// moeda</strong>. Um recibo que misturasse clientes não teria a quem ser
/// passado, e um que misturasse moedas somaria quantias que não se somam.
/// </para>
/// </summary>
public sealed class RegisterReceipt(
    ISalesInvoiceStore store,
    IAuditTrail audit,
    PostDocument posting,
    TimeProvider clock,
    IOptions<FinanceOptions> options)
{
    private readonly FinanceOptions _options = options.Value;

    public async Task<RegisterReceiptResult> ExecuteAsync(
        string seriesCode,
        DateOnly receivedOn,
        PaymentMethod method,
        IReadOnlyList<SettlementInput> settlements,
        string? notes,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        if (settlements is null || settlements.Count == 0)
        {
            return RegisterReceiptResult.Rejected("Um recibo diz sempre a que factura o dinheiro foi.");
        }

        var liquidacoes = new List<NewSettlement>(settlements.Count);
        SalesInvoice? primeira = null;

        foreach (var pedido in settlements)
        {
            var factura = await store.FindForUpdateAsync(pedido.SalesInvoiceId, cancellationToken);

            if (factura is null)
            {
                return RegisterReceiptResult.InvoiceNotFound();
            }

            if (factura.Status is InvoiceStatus.Cancelled)
            {
                return RegisterReceiptResult.Rejected(
                    $"A factura {factura.Number.Formatted} está anulada e não se recebe.");
            }

            primeira ??= factura;

            if (factura.CustomerId != primeira.CustomerId)
            {
                return RegisterReceiptResult.Rejected(
                    "Todas as facturas de um recibo têm de ser do mesmo cliente.");
            }

            if (!string.Equals(factura.Currency, primeira.Currency, StringComparison.Ordinal))
            {
                return RegisterReceiptResult.Rejected(
                    "Todas as facturas de um recibo têm de ser na mesma moeda.");
            }

            // Receber mais do que se deve não é um recebimento — é um
            // adiantamento, e esse é outro documento que não existe.
            var emAberto = await store.OutstandingAsync(factura.Id, cancellationToken);

            if (pedido.Amount > emAberto)
            {
                return RegisterReceiptResult.ExceedsOutstanding(
                    $"A factura {factura.Number.Formatted} tem {emAberto:N2} em aberto e " +
                    $"recebeu-se {pedido.Amount:N2}.");
            }

            liquidacoes.Add(new NewSettlement(factura.Id, factura.Number.Formatted, pedido.Amount));
        }

        var serie = await store.FindSeriesForAllocationAsync(
            DocumentType.RG,
            IssueCreditNote.NormalizeSeries(seriesCode, _options.DefaultSeries),
            cancellationToken);

        if (serie is null)
        {
            return RegisterReceiptResult.SeriesNotFound();
        }

        Receipt recibo;

        try
        {
            recibo = Receipt.Register(
                serie.Allocate(),
                receivedOn,
                primeira!.CustomerId,
                primeira.Customer,
                primeira.Currency,
                method,
                liquidacoes,
                notes,
                _options.FiscalNotice);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return RegisterReceiptResult.Rejected(error.Message);
        }

        await store.AddReceiptAsync(recibo, cancellationToken);

        // Num recibo não há imposto a separar: o total é o líquido, e a regra
        // equilibra na mesma porque tirar a mesma parcela dos dois lados
        // mantém a igualdade.
        var lancamento = await posting.PostAsync(
            new DocumentPosting(
                PostingEvent.ReceiptRegistered,
                recibo.Number.Formatted,
                recibo.Number.Formatted,
                $"Recebimento de {recibo.Customer.Name}",
                recibo.ReceivedOn,
                recibo.Total,
                0m,
                recibo.Total,
                PostingSources.Automatic,
                clock.GetUtcNow()),
            cancellationToken);

        if (lancamento.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return RegisterReceiptResult.PostingBlocked(lancamento.Error!);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.ReceiptRegistered,
                FinanceAuditEntityTypes.Receipt,
                recibo.Id.ToString(),
                context,
                NewValue: $$"""
                    {"number":"{{recibo.Number.Formatted}}","total":{{recibo.Total}},"currency":"{{recibo.Currency}}","method":"{{recibo.Method}}"}
                    """),
            cancellationToken);

        return RegisterReceiptResult.Success(recibo.Id, recibo.Number.Formatted);
    }
}

public sealed record SettlementInput(Guid SalesInvoiceId, decimal Amount);

public sealed record RegisterReceiptResult(
    RegisterReceiptOutcome Outcome,
    Guid? ReceiptId,
    string? Number,
    string? Error)
{
    public static RegisterReceiptResult Success(Guid id, string number) =>
        new(RegisterReceiptOutcome.Registered, id, number, null);

    public static RegisterReceiptResult Rejected(string error) =>
        new(RegisterReceiptOutcome.Rejected, null, null, error);

    public static RegisterReceiptResult InvoiceNotFound() =>
        new(RegisterReceiptOutcome.InvoiceNotFound, null, null, null);

    public static RegisterReceiptResult SeriesNotFound() =>
        new(RegisterReceiptOutcome.SeriesNotFound, null, null, null);

    /// <summary>Postagem automática ligada e falhada. O recibo não é registado.</summary>
    public static RegisterReceiptResult PostingBlocked(string error) =>
        new(RegisterReceiptOutcome.PostingBlocked, null, null, error);

    public static RegisterReceiptResult ExceedsOutstanding(string error) =>
        new(RegisterReceiptOutcome.ExceedsOutstanding, null, null, error);
}

public enum RegisterReceiptOutcome
{
    Registered,
    Rejected,
    InvoiceNotFound,
    SeriesNotFound,

    /// <summary>Recebe mais do que está em aberto — 409.</summary>
    ExceedsOutstanding,

    /// <summary>Contabilidade automática ligada e a postagem falhou — 409.</summary>
    PostingBlocked,
}
