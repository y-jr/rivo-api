using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Notifications.Application;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Notifications.Api;

public static class NotificationsModuleEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/notifications");

        // Só autenticação, sem permissão: o que limita o acesso é ser o
        // destinatário, e isso é invariante do domínio.
        group.MapGet("/me", ListMineAsync).RequireAuthorization()
            .Produces<IReadOnlyList<NotificationView>>()
            .ProducesValidationProblem();

        group.MapPost("/{notificationId:guid}/read", MarkAsReadAsync).RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Rota propria em vez de um parametro no anterior: marcar uma e marcar
        // todas sao actos diferentes, e a forma diz qual foi.
        group.MapPost("/read-all", MarkAllAsReadAsync).RequireAuthorization()
            .Produces(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>
    /// Identificador do utilizador autenticado. Nunca vem do pedido — se
    /// viesse, qualquer pessoa leria as notificações de outra.
    /// </summary>
    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static async Task<IResult> ListMineAsync(
        ListMyNotifications list,
        ClaimsPrincipal principal,
        HttpResponse response,
        CancellationToken cancellationToken,
        bool unreadOnly = false,
        int limit = 50,
        int? page = null,
        int? pageSize = null)
    {
        var userId = CurrentUserId(principal);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (!Pagination.TryParse(page, pageSize, out var pagina, out var erro))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagina"] = [erro!] });
        }

        var (itens, total) = await list.ExecuteAsync(userId.Value, unreadOnly, limit, pagina, cancellationToken);

        if (pagina is { } p)
        {
            response.Headers["X-Page"] = p.Page.ToString();
            response.Headers["X-Page-Size"] = p.PageSize.ToString();
            response.Headers["X-Total-Count"] = total!.Value.ToString();
        }

        return Results.Ok(itens);
    }

    private static async Task<IResult> MarkAllAsReadAsync(
        MarkAllNotificationsAsRead markAllAsRead,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId(principal);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var marcadas = await markAllAsRead.ExecuteAsync(userId.Value, cancellationToken);

        // Devolve quantas ficaram marcadas em vez de 204: o cliente acabou de
        // mostrar um contador, e assim confirma-o sem voltar a pedir a lista.
        return Results.Ok(new { marcadas });
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid notificationId,
        MarkNotificationAsRead markAsRead,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId(principal);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var marked = await markAsRead.ExecuteAsync(notificationId, userId.Value, cancellationToken);

        // 404 tanto para inexistente como para alheia: distinguir revelaria a
        // existência de notificações de outros utilizadores.
        return marked ? Results.NoContent() : Results.NotFound();
    }
}
