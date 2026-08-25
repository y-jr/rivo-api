using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
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

        return endpoints;
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
