using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Notifications.Application;

namespace Rivo.Notifications.Api;

public static class NotificationsModuleEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/notifications");

        // Só autenticação, sem permissão: o que limita o acesso é ser o
        // destinatário, e isso é invariante do domínio.
        group.MapGet("/me", ListMineAsync).RequireAuthorization();
        group.MapPost("/{notificationId:guid}/read", MarkAsReadAsync).RequireAuthorization();

        // Rota propria em vez de um parametro no anterior: marcar uma e marcar
        // todas sao actos diferentes, e a forma diz qual foi.
        group.MapPost("/read-all", MarkAllAsReadAsync).RequireAuthorization();

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
        CancellationToken cancellationToken,
        bool unreadOnly = false,
        int limit = 50)
    {
        var userId = CurrentUserId(principal);

        return userId is null
            ? Results.Unauthorized()
            : Results.Ok(await list.ExecuteAsync(userId.Value, unreadOnly, limit, cancellationToken));
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
