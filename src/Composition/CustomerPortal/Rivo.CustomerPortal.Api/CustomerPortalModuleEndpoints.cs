using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.CustomerPortal.Application;

namespace Rivo.CustomerPortal.Api;

public static class CustomerPortalModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso. Vive aqui — em `Api`, não em `Infrastructure`
    /// — porque a camada de composição não tem uma (ADR-041).
    /// </summary>
    public static IServiceCollection AddCustomerPortalModule(this IServiceCollection services)
    {
        services.AddScoped<GetMyOverview>();
        services.AddScoped<GetMyStatement>();
        services.AddScoped<SubmitPaymentProof>();
        services.AddScoped<ListMyPaymentClaims>();
        services.AddScoped<SendMessage>();
        services.AddScoped<ListMyMessages>();

        return services;
    }

    public static IEndpointRouteBuilder MapCustomerPortalModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/customer-portal");

        // Sem permissão nenhuma — só autenticação. "Próprio" não é uma
        // operação que se atribui a um perfil (ADR-042/ADR-043); é
        // consequência de estar autenticado, seja qual for o perfil. Nunca
        // aceita `customerId`: devolve sempre e só o cliente do próprio
        // chamador.
        group.MapGet("/me", GetMyOverviewAsync).RequireAuthorization();

        group.MapGet("/me/statement", GetMyStatementAsync).RequireAuthorization();

        // Comprovativo de pagamento (ADR-044) — mesma disciplina de "o
        // próprio": nunca aceita `customerId`, resolve-o do token.
        group.MapPost("/me/payment-claims", SubmitPaymentProofAsync).RequireAuthorization();

        group.MapGet("/me/payment-claims", ListMyPaymentClaimsAsync).RequireAuthorization();

        // Mensagens à equipa comercial (ADR-045) — mesma disciplina de "o
        // próprio": nunca aceita `customerId`, resolve-o do token.
        group.MapPost("/me/messages", SendMessageAsync).RequireAuthorization();

        group.MapGet("/me/messages", ListMyMessagesAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetMyOverviewAsync(
        HttpContext http,
        GetMyOverview getMyOverview,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken,
        string currency = "AOA")
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            // Autenticado, mas sem identificador reconhecível no token — não
            // deveria acontecer com um token emitido pelo Rivo, mas
            // recusa-se em vez de adivinhar (mesma disciplina de ADR-042).
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await getMyOverview.ExecuteAsync(userId, from, to, currency, cancellationToken);

        return result.Outcome switch
        {
            MyOverviewOutcome.Found => Results.Ok(result.Overview),

            MyOverviewOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            MyOverviewOutcome.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["janela"] = [result.Error!] }),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> GetMyStatementAsync(
        HttpContext http,
        GetMyStatement getMyStatement,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken,
        string currency = "AOA")
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await getMyStatement.ExecuteAsync(userId, from, to, currency, cancellationToken);

        return result.Outcome switch
        {
            MyStatementOutcome.Found => Results.Ok(result.Statement),

            MyStatementOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            MyStatementOutcome.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["janela"] = [result.Error!] }),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> SubmitPaymentProofAsync(
        HttpContext http,
        SubmitPaymentProof submit,
        SubmitPaymentProofRequest request,
        CancellationToken cancellationToken)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await submit.ExecuteAsync(
            userId, request.SalesInvoiceId, request.Amount, request.PaidOn, request.DocumentId,
            request.Notes, cancellationToken);

        return result.Outcome switch
        {
            SubmitPaymentProofOutcome.Submitted => Results.Created(
                $"/customer-portal/me/payment-claims", new { claimId = result.ClaimId }),

            SubmitPaymentProofOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            SubmitPaymentProofOutcome.InvoiceNotFound =>
                Results.NotFound(new { erro = result.Error }),

            SubmitPaymentProofOutcome.DocumentNotFound =>
                Results.NotFound(new { erro = result.Error }),

            SubmitPaymentProofOutcome.ExceedsOutstanding =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            SubmitPaymentProofOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["pedido"] = [result.Error!] }),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> ListMyPaymentClaimsAsync(
        HttpContext http,
        ListMyPaymentClaims listClaims,
        CancellationToken cancellationToken)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await listClaims.ExecuteAsync(userId, cancellationToken);

        return result.Outcome switch
        {
            ListMyPaymentClaimsOutcome.Found => Results.Ok(result.Claims),

            ListMyPaymentClaimsOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> SendMessageAsync(
        HttpContext http,
        SendMessage send,
        SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await send.ExecuteAsync(userId, request.Body, cancellationToken);

        return result.Outcome switch
        {
            SendMessageOutcome.Sent => Results.Created(
                "/customer-portal/me/messages",
                new { conversationId = result.ConversationId, messageId = result.MessageId }),

            SendMessageOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            SendMessageOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [result.Error!] }),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> ListMyMessagesAsync(
        HttpContext http,
        ListMyMessages listMessages,
        CancellationToken cancellationToken)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await listMessages.ExecuteAsync(userId, cancellationToken);

        return result.Outcome switch
        {
            ListMyMessagesOutcome.Found => Results.Ok(result.Conversations),

            ListMyMessagesOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum cliente.",
                statusCode: StatusCodes.Status403Forbidden),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }
}

public sealed record SendMessageRequest(string Body);

/// <param name="PaidOn">A data que o cliente diz ter pago — a que o recibo, quando confirmado, herda.</param>
/// <param name="DocumentId">O comprovativo, já carregado por <c>POST /documents</c> (permissão <c>documents.write</c>).</param>
public sealed record SubmitPaymentProofRequest(
    Guid SalesInvoiceId,
    decimal Amount,
    DateOnly PaidOn,
    Guid DocumentId,
    string? Notes);
