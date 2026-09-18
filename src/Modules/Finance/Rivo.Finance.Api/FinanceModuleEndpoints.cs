using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Domain;
using Rivo.Finance.Contracts;

namespace Rivo.Finance.Api;

public static class FinanceModuleEndpoints
{
    public static IEndpointRouteBuilder MapFinanceModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/finance");

        group.MapGet("/series", ListSeriesAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        // Abrir uma série paralela é a forma óbvia de emitir fora da sequência
        // auditável. Permissão própria, só Admin.
        group.MapPost("/series", OpenSeriesAsync)
            .RequireAuthorization(FinancePermissions.SeriesWrite);

        group.MapGet("/sales-invoices", ListAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        group.MapGet("/sales-invoices/{invoiceId:guid}", GetAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        group.MapPost("/sales-invoices", IssueAsync)
            .RequireAuthorization(FinancePermissions.InvoicesWrite);

        // Anular, nunca eliminar (BR-14). Não há DELETE nesta superfície, e a
        // permissão é distinta de emitir: desfazer não é a mesma autorização
        // que fazer.
        group.MapPost("/sales-invoices/{invoiceId:guid}/cancellation", CancelAsync)
            .RequireAuthorization(FinancePermissions.InvoicesCancel);

        // O saldo: o que falta receber, e de onde vem esse número.
        group.MapGet("/sales-invoices/{invoiceId:guid}/balance", BalanceAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        // --- O papel dos documentos (ADR-066, fecha o K23) ---
        //
        // `/document` devolve o PDF; `/delivery` envia-o ao cliente. Dois
        // recursos e não um parâmetro, porque são actos diferentes com
        // permissões diferentes — ver `FinancePermissions.DocumentsDeliver`.
        group.MapGet("/sales-invoices/{invoiceId:guid}/document", GetInvoiceDocumentAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        group.MapPost("/sales-invoices/{invoiceId:guid}/delivery", DeliverInvoiceAsync)
            .RequireAuthorization(FinancePermissions.DocumentsDeliver);

        // Nota de crédito. **Anular não é a mesma coisa:** anular apaga a
        // factura inteira do mapa de dívida; creditar reduz o que ela pede e
        // deixa rasto do quanto e do porquê.
        group.MapGet("/credit-notes", ListCreditNotesAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        group.MapGet("/credit-notes/{creditNoteId:guid}", GetCreditNoteAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        // Emitir uma nota de crédito é devolver valor. Fica com a permissão de
        // anular, não com a de emitir: quem factura no dia-a-dia não decide
        // sozinho reduzir o que se cobra.
        group.MapPost("/credit-notes", IssueCreditNoteAsync)
            .RequireAuthorization(FinancePermissions.InvoicesCancel);

        group.MapPost("/credit-notes/{creditNoteId:guid}/cancellation", CancelCreditNoteAsync)
            .RequireAuthorization(FinancePermissions.InvoicesCancel);

        group.MapGet("/credit-notes/{creditNoteId:guid}/document", GetCreditNoteDocumentAsync)
            .RequireAuthorization(FinancePermissions.InvoicesRead);

        group.MapPost("/credit-notes/{creditNoteId:guid}/delivery", DeliverCreditNoteAsync)
            .RequireAuthorization(FinancePermissions.DocumentsDeliver);

        // Recibos.
        group.MapGet("/receipts", ListReceiptsAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsRead);

        group.MapGet("/receipts/{receiptId:guid}", GetReceiptAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsRead);

        group.MapPost("/receipts", RegisterReceiptAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsWrite);

        // Estornar um recebimento faz a dívida voltar a existir. Permissão de
        // anulação, não de registo.
        group.MapPost("/receipts/{receiptId:guid}/cancellation", CancelReceiptAsync)
            .RequireAuthorization(FinancePermissions.InvoicesCancel);

        group.MapGet("/receipts/{receiptId:guid}/document", GetReceiptDocumentAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsRead);

        group.MapPost("/receipts/{receiptId:guid}/delivery", DeliverReceiptAsync)
            .RequireAuthorization(FinancePermissions.DocumentsDeliver);

        // Pedidos de confirmação de pagamento (ADR-044) — a submissão em si
        // não tem endpoint aqui: passa sempre pelo Portal do Cliente, que
        // resolve "o próprio cliente" antes de chegar a `finance`.
        group.MapGet("/payment-claims", ListPaymentClaimsAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsRead);

        // Confirmar dispara o recibo (RegisterReceipt) — mesma permissão de
        // registar um, porque é o que isto faz de facto.
        group.MapPost("/payment-claims/{claimId:guid}/confirmation", ConfirmPaymentClaimAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsWrite);

        group.MapPost("/payment-claims/{claimId:guid}/rejection", RejectPaymentClaimAsync)
            .RequireAuthorization(FinancePermissions.ReceiptsWrite);

        return endpoints;
    }

    private static async Task<IResult> BalanceAsync(
        Guid invoiceId,
        GetInvoiceBalance getBalance,
        CancellationToken cancellationToken)
    {
        var saldo = await getBalance.ExecuteAsync(invoiceId, cancellationToken);

        return saldo is null
            ? Results.NotFound(new { erro = "Factura não encontrada." })
            : Results.Ok(saldo);
    }

    // --- O papel dos documentos (ADR-066) ---
    //
    // Seis rotas, dois handlers genéricos. Factura, nota de crédito e recibo
    // diferem no tipo e em mais nada aqui — a projecção comum está no caso de
    // uso, e repetir a tradução HTTP três vezes só criava três sítios para ela
    // divergir.

    private static Task<IResult> GetInvoiceDocumentAsync(
        Guid invoiceId,
        IssueFiscalDocumentFile ficheiros,
        HttpContext http,
        CancellationToken cancellationToken) =>
        DocumentoAsync(FiscalDocumentKind.SalesInvoice, invoiceId, ficheiros, http, cancellationToken);

    private static Task<IResult> GetCreditNoteDocumentAsync(
        Guid creditNoteId,
        IssueFiscalDocumentFile ficheiros,
        HttpContext http,
        CancellationToken cancellationToken) =>
        DocumentoAsync(FiscalDocumentKind.CreditNote, creditNoteId, ficheiros, http, cancellationToken);

    private static Task<IResult> GetReceiptDocumentAsync(
        Guid receiptId,
        IssueFiscalDocumentFile ficheiros,
        HttpContext http,
        CancellationToken cancellationToken) =>
        DocumentoAsync(FiscalDocumentKind.Receipt, receiptId, ficheiros, http, cancellationToken);

    private static Task<IResult> DeliverInvoiceAsync(
        Guid invoiceId,
        DeliverFiscalDocument entrega,
        HttpContext http,
        CancellationToken cancellationToken) =>
        EntregaAsync(FiscalDocumentKind.SalesInvoice, invoiceId, entrega, http, cancellationToken);

    private static Task<IResult> DeliverCreditNoteAsync(
        Guid creditNoteId,
        DeliverFiscalDocument entrega,
        HttpContext http,
        CancellationToken cancellationToken) =>
        EntregaAsync(FiscalDocumentKind.CreditNote, creditNoteId, entrega, http, cancellationToken);

    private static Task<IResult> DeliverReceiptAsync(
        Guid receiptId,
        DeliverFiscalDocument entrega,
        HttpContext http,
        CancellationToken cancellationToken) =>
        EntregaAsync(FiscalDocumentKind.Receipt, receiptId, entrega, http, cancellationToken);

    private static async Task<IResult> DocumentoAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        IssueFiscalDocumentFile ficheiros,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await ficheiros.ExecuteAsync(
            kind, documentId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            FiscalDocumentFileOutcome.Ready => Results.File(
                result.Content!,
                "application/pdf",
                result.FileName,

                // `inline` e não `attachment`: o caso corrente é ver o documento
                // antes de decidir se se envia. O browser guarda-o com o nome
                // certo de qualquer maneira, se o utilizador quiser.
                enableRangeProcessing: false),

            FiscalDocumentFileOutcome.DocumentNotFound =>
                Results.NotFound(new { erro = "Documento não encontrado." }),

            // 501 e não 500: a capacidade existe, falta configurar quem emite.
            // Mesma leitura do 501 em `hr` e `procurement` — é estado de
            // configuração, não avaria.
            FiscalDocumentFileOutcome.IssuerNotDeclared => Results.Problem(
                "A identidade fiscal da empresa não foi declarada. "
                + "Sem emitente não há documento fiscal — ver PUT /fiscal/tax-entity.",
                statusCode: StatusCodes.Status501NotImplemented),

            _ => Results.Problem("Resultado inesperado ao compor o documento."),
        };
    }

    private static async Task<IResult> EntregaAsync(
        FiscalDocumentKind kind,
        Guid documentId,
        DeliverFiscalDocument entrega,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await entrega.ExecuteAsync(
            kind, documentId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            FiscalDocumentDeliveryOutcome.Sent => Results.Ok(new { enviadoPara = result.SentTo }),

            FiscalDocumentDeliveryOutcome.DocumentNotFound =>
                Results.NotFound(new { erro = "Documento não encontrado." }),

            // 409: o documento está bem, o que falta é a quem enviar. Não é o
            // pedido que está malformado — é o cliente que não tem endereço, ou
            // não há cliente nenhum (consumidor final).
            FiscalDocumentDeliveryOutcome.NoRecipient =>
                Results.Conflict(new { erro = result.Error }),

            FiscalDocumentDeliveryOutcome.IssuerNotDeclared => Results.Problem(
                "A identidade fiscal da empresa não foi declarada — ver PUT /fiscal/tax-entity.",
                statusCode: StatusCodes.Status501NotImplemented),

            // 503: o correio é dependência externa e falhou. A mensagem do
            // servidor sobe para quem clicou poder agir.
            FiscalDocumentDeliveryOutcome.DeliveryFailed => Results.Problem(
                result.Error,
                statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => Results.Problem("Resultado inesperado ao entregar o documento."),
        };
    }

    private static async Task<IResult> ListCreditNotesAsync(
        ListCreditNotes listNotes,
        Guid? salesInvoiceId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listNotes.ExecuteAsync(salesInvoiceId, cancellationToken));

    private static async Task<IResult> GetCreditNoteAsync(
        Guid creditNoteId,
        GetCreditNote getNote,
        CancellationToken cancellationToken)
    {
        var nota = await getNote.ExecuteAsync(creditNoteId, cancellationToken);

        return nota is null
            ? Results.NotFound(new { erro = "Nota de crédito não encontrada." })
            : Results.Ok(nota);
    }

    private static async Task<IResult> IssueCreditNoteAsync(
        IssueCreditNoteRequest request,
        IssueCreditNote issueNote,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var linhas = (request.Lines ?? [])
            .Select(line => new InvoiceLineInput(
                line.Description, line.Quantity, line.UnitPrice, line.TaxCode))
            .ToList();

        var result = await issueNote.ExecuteAsync(
            request.SalesInvoiceId,
            request.Series ?? string.Empty,
            request.IssuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            request.Reason ?? string.Empty,
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            IssueCreditNoteOutcome.Issued => Results.Created(
                $"/finance/credit-notes/{result.CreditNoteId}",
                new { creditNoteId = result.CreditNoteId, number = result.Number }),

            IssueCreditNoteOutcome.InvoiceNotFound =>
                Results.NotFound(new { erro = "Factura não encontrada." }),

            IssueCreditNoteOutcome.SeriesNotFound =>
                Results.NotFound(new { erro = "Série NC não encontrada. Abra-a em /finance/series." }),

            // 409: creditar mais do que está em aberto é conflito com o estado
            // da factura, não campo mal preenchido.
            IssueCreditNoteOutcome.ExceedsOutstanding =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            IssueCreditNoteOutcome.ExemptionUnavailable => Results.Problem(
                "Creditar linha isenta exige o catálogo de códigos de isenção, que ainda não existe (ADR-036).",
                statusCode: StatusCodes.Status501NotImplemented),

            IssueCreditNoteOutcome.PostingBlocked =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            IssueCreditNoteOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["nota"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao emitir a nota de crédito."),
        };
    }

    private static async Task<IResult> CancelCreditNoteAsync(
        Guid creditNoteId,
        CancelInvoiceRequest request,
        CancelCreditNote cancelNote,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancelNote.ExecuteAsync(
            creditNoteId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CancelInvoiceOutcome.Cancelled => Results.NoContent(),
            CancelInvoiceOutcome.NotFound => Results.NotFound(new { erro = "Nota de crédito não encontrada." }),
            CancelInvoiceOutcome.Rejected =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem("Resultado inesperado ao anular a nota de crédito."),
        };
    }

    private static async Task<IResult> ListReceiptsAsync(
        ListReceipts listReceipts,
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken) =>
        Results.Ok(await listReceipts.ExecuteAsync(customerId, from, to, cancellationToken));

    private static async Task<IResult> GetReceiptAsync(
        Guid receiptId,
        GetReceipt getReceipt,
        CancellationToken cancellationToken)
    {
        var recibo = await getReceipt.ExecuteAsync(receiptId, cancellationToken);

        return recibo is null
            ? Results.NotFound(new { erro = "Recibo não encontrado." })
            : Results.Ok(recibo);
    }

    private static async Task<IResult> RegisterReceiptAsync(
        RegisterReceiptRequest request,
        RegisterReceipt registerReceipt,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var meio))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["method"] =
                [
                    $"'{request.Method}' não é meio de pagamento do SAF-T. " +
                    $"Válidos: {string.Join(", ", Enum.GetNames<PaymentMethod>())}.",
                ],
            });
        }

        var liquidacoes = (request.Settlements ?? [])
            .Select(s => new SettlementInput(s.SalesInvoiceId, s.Amount))
            .ToList();

        var result = await registerReceipt.ExecuteAsync(
            request.Series ?? string.Empty,
            request.ReceivedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            meio,
            liquidacoes,
            request.Notes,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            RegisterReceiptOutcome.Registered => Results.Created(
                $"/finance/receipts/{result.ReceiptId}",
                new { receiptId = result.ReceiptId, number = result.Number }),

            RegisterReceiptOutcome.InvoiceNotFound =>
                Results.NotFound(new { erro = "Factura não encontrada." }),

            RegisterReceiptOutcome.SeriesNotFound =>
                Results.NotFound(new { erro = "Série RG não encontrada. Abra-a em /finance/series." }),

            RegisterReceiptOutcome.ExceedsOutstanding =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            RegisterReceiptOutcome.PostingBlocked =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            RegisterReceiptOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["recibo"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao registar o recibo."),
        };
    }

    private static async Task<IResult> CancelReceiptAsync(
        Guid receiptId,
        CancelInvoiceRequest request,
        CancelReceipt cancelReceipt,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancelReceipt.ExecuteAsync(
            receiptId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CancelInvoiceOutcome.Cancelled => Results.NoContent(),
            CancelInvoiceOutcome.NotFound => Results.NotFound(new { erro = "Recibo não encontrado." }),
            CancelInvoiceOutcome.Rejected =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem("Resultado inesperado ao estornar o recibo."),
        };
    }

    private static async Task<IResult> ListPaymentClaimsAsync(
        ListPaymentClaims listClaims,
        Guid? customerId,
        PaymentClaimStatus? status,
        CancellationToken cancellationToken) =>
        Results.Ok(await listClaims.ExecuteAsync(customerId, status, cancellationToken));

    private static async Task<IResult> ConfirmPaymentClaimAsync(
        Guid claimId,
        ConfirmPaymentClaim confirm,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var contexto = BuildAuditContext(http);
        var result = await confirm.ExecuteAsync(claimId, contexto.ActorId ?? Guid.Empty, contexto, cancellationToken);

        return result.Outcome switch
        {
            ReviewPaymentClaimOutcome.Confirmed => Results.Ok(new { receiptId = result.ReceiptId }),

            ReviewPaymentClaimOutcome.NotFound =>
                Results.NotFound(new { erro = "Pedido não encontrado." }),

            ReviewPaymentClaimOutcome.Rejected or ReviewPaymentClaimOutcome.ReceiptFailed =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem("Resultado inesperado ao confirmar o pedido."),
        };
    }

    private static async Task<IResult> RejectPaymentClaimAsync(
        Guid claimId,
        CancelInvoiceRequest request,
        RejectPaymentClaim reject,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var contexto = BuildAuditContext(http);
        var result = await reject.ExecuteAsync(
            claimId, request.Reason, contexto.ActorId ?? Guid.Empty, contexto, cancellationToken);

        return result.Outcome switch
        {
            ReviewPaymentClaimOutcome.RejectedOk => Results.NoContent(),

            ReviewPaymentClaimOutcome.NotFound =>
                Results.NotFound(new { erro = "Pedido não encontrado." }),

            ReviewPaymentClaimOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao rejeitar o pedido."),
        };
    }

    private static async Task<IResult> ListSeriesAsync(
        ListDocumentSeries listSeries,
        CancellationToken cancellationToken) =>
        Results.Ok(await listSeries.ExecuteAsync(cancellationToken));

    private static async Task<IResult> OpenSeriesAsync(
        OpenSeriesRequest request,
        OpenDocumentSeries openSeries,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openSeries.ExecuteAsync(
            request.Code, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            OpenSeriesOutcome.Opened =>
                Results.Created($"/finance/series/{result.SeriesId}", new { seriesId = result.SeriesId }),

            OpenSeriesOutcome.Duplicate =>
                Results.Conflict(new { erro = "Já existe uma série FT com este código." }),

            OpenSeriesOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["serie"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao abrir a série."),
        };
    }

    private static async Task<IResult> ListAsync(
        ListSalesInvoices listInvoices,
        Guid? customerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken) =>
        Results.Ok(await listInvoices.ExecuteAsync(customerId, from, to, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid invoiceId,
        GetSalesInvoice getInvoice,
        CancellationToken cancellationToken)
    {
        var factura = await getInvoice.ExecuteAsync(invoiceId, cancellationToken);

        return factura is null
            ? Results.NotFound(new { erro = "Factura não encontrada." })
            : Results.Ok(factura);
    }

    private static async Task<IResult> IssueAsync(
        IssueInvoiceRequest request,
        IssueSalesInvoice issueInvoice,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var linhas = (request.Lines ?? [])
            .Select(line => new InvoiceLineInput(
                line.Description, line.Quantity, line.UnitPrice, line.TaxCode))
            .ToList();

        var result = await issueInvoice.ExecuteAsync(
            request.CustomerId,
            request.Series ?? string.Empty,
            request.IssuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            request.TaxPointDate,
            request.Currency ?? "AOA",
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            IssueInvoiceOutcome.Issued => Results.Created(
                $"/finance/sales-invoices/{result.InvoiceId}",
                new { invoiceId = result.InvoiceId, number = result.Number }),

            IssueInvoiceOutcome.CustomerNotFound =>
                Results.NotFound(new { erro = "Cliente não encontrado." }),

            IssueInvoiceOutcome.SeriesNotFound =>
                Results.NotFound(new { erro = "Série de numeração não encontrada. Abra-a em /finance/series." }),

            // 501 e não 400: o pedido é legítimo e o sistema é que não sabe
            // emiti-lo. Falta o catálogo de códigos de isenção (ADR-036), e não
            // se inventa código.
            IssueInvoiceOutcome.ExemptionUnavailable => Results.Problem(
                "Emitir com isenção exige o catálogo de códigos de isenção, que ainda não existe (ADR-036).",
                statusCode: StatusCodes.Status501NotImplemented),

            // 409: a contabilidade automatica esta ligada e a postagem nao passou.
            // **Nem factura nem lancamento foram gravados** — a transaccao leva os
            // dois, e um documento emitido que nao lancou seria um buraco nos
            // livros que ninguem ve.
            IssueInvoiceOutcome.PostingBlocked =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            IssueInvoiceOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["factura"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao emitir a factura."),
        };
    }

    private static async Task<IResult> CancelAsync(
        Guid invoiceId,
        CancelInvoiceRequest request,
        CancelSalesInvoice cancelInvoice,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancelInvoice.ExecuteAsync(
            invoiceId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CancelInvoiceOutcome.Cancelled => Results.NoContent(),

            CancelInvoiceOutcome.NotFound => Results.NotFound(new { erro = "Factura não encontrada." }),

            // 409: já anulada é conflito de estado, não pedido mal formado.
            CancelInvoiceOutcome.Rejected =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem("Resultado inesperado ao anular a factura."),
        };
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }
}

public sealed record OpenSeriesRequest(string Code);

/// <param name="TaxPointDate">
/// Data do facto gerador. Omitida, assume-se a data do documento — o caso
/// corrente. É ela que determina a taxa (ADR-011 §3).
/// </param>
/// <param name="CustomerId">
/// Omitido ou nulo, a factura sai a **consumidor final** — venda a quem não se
/// identificou. Não é campo esquecido: é uma escolha, e o documento fica com a
/// designação convencionada em vez de um cliente registado.
/// </param>
public sealed record IssueInvoiceRequest(
    Guid? CustomerId,
    string? Series,
    DateOnly? IssuedOn,
    DateOnly? TaxPointDate,
    string? Currency,
    IReadOnlyList<InvoiceLineRequest>? Lines);

public sealed record InvoiceLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode);

public sealed record CancelInvoiceRequest(string Reason);

/// <param name="Reason">Porquê. Obrigatório — é a primeira coisa que uma conferência pergunta.</param>
public sealed record IssueCreditNoteRequest(
    Guid SalesInvoiceId,
    string? Series,
    DateOnly? IssuedOn,
    string? Reason,
    IReadOnlyList<InvoiceLineRequest>? Lines);

/// <param name="Method">
/// Meio de pagamento do SAF-T: NU, TB, CH, CC, CD, MB, PR, CS, DE, OU.
/// `MB` é Multicaixa.
/// </param>
public sealed record RegisterReceiptRequest(
    string? Series,
    DateOnly? ReceivedOn,
    string Method,
    string? Notes,
    IReadOnlyList<SettlementRequest>? Settlements);

public sealed record SettlementRequest(Guid SalesInvoiceId, decimal Amount);
