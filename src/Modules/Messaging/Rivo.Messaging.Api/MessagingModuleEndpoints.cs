using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Messaging.Application.UseCases;
using Rivo.Messaging.Contracts;
using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Api;

public static class MessagingModuleEndpoints
{
    public static IEndpointRouteBuilder MapMessagingModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/messaging");

        // A fila partilhada — visível a quem tiver a permissão, atribuída ou
        // não (ADR-045 §2). O envio em si não tem endpoint aqui: passa
        // sempre pelo Portal do Cliente, que resolve "o próprio cliente"
        // antes de chegar a `messaging`.
        group.MapGet("/conversations", ListAsync)
            .RequireAuthorization(MessagingPermissions.ConversationsRead);

        group.MapGet("/conversations/{conversationId:guid}", GetAsync)
            .RequireAuthorization(MessagingPermissions.ConversationsRead);

        group.MapPost("/conversations/{conversationId:guid}/messages", ReplyAsync)
            .RequireAuthorization(MessagingPermissions.ConversationsWrite);

        group.MapPost("/conversations/{conversationId:guid}/closure", CloseAsync)
            .RequireAuthorization(MessagingPermissions.ConversationsWrite);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListConversations listConversations,
        ConversationStatus? status,
        CancellationToken cancellationToken) =>
        Results.Ok(await listConversations.ExecuteAsync(status, cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid conversationId,
        GetConversation getConversation,
        CancellationToken cancellationToken)
    {
        var conversa = await getConversation.ExecuteAsync(conversationId, cancellationToken);

        return conversa is null
            ? Results.NotFound(new { erro = "Conversa não encontrada." })
            : Results.Ok(conversa);
    }

    private static async Task<IResult> ReplyAsync(
        Guid conversationId,
        ReplyRequest request,
        SendEmployeeReply reply,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var contexto = BuildAuditContext(http);

        if (contexto.ActorId is not { } senderUserId)
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await reply.ExecuteAsync(conversationId, senderUserId, request.Body, contexto, cancellationToken);

        return result.Outcome switch
        {
            ReplyOutcome.Sent => Results.Created(
                $"/messaging/conversations/{conversationId}", new { messageId = result.MessageId }),

            ReplyOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            ReplyOutcome.Closed => Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            ReplyOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao responder."),
        };
    }

    private static async Task<IResult> CloseAsync(
        Guid conversationId,
        CloseConversation close,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var contexto = BuildAuditContext(http);

        if (contexto.ActorId is not { } closedByUserId)
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var outcome = await close.ExecuteAsync(conversationId, closedByUserId, contexto, cancellationToken);

        return outcome switch
        {
            CloseConversationOutcome.Closed => Results.NoContent(),

            CloseConversationOutcome.NotFound => Results.NotFound(new { erro = "Conversa não encontrada." }),

            CloseConversationOutcome.AlreadyClosed =>
                Results.Problem("Esta conversa já está fechada.", statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem("Resultado inesperado ao fechar a conversa."),
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

public sealed record ReplyRequest(string Body);
