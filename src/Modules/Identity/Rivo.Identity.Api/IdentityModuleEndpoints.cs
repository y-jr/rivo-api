using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Identity.Api.Contracts;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Identity.Application.UseCases;

namespace Rivo.Identity.Api;

public static class IdentityModuleEndpoints
{
    /// <summary>
    /// Namespace de rotas do módulo. Cada módulo expõe a sua superfície sob o
    /// seu próprio prefixo; o host não agrega endpoints de módulos.
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/identity");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LogInAsync);
        group.MapPost("/login/google", LogInWithGoogleAsync);
        group.MapPost("/logout", LogOutAsync).RequireAuthorization();
        group.MapGet("/me", GetCurrentUser).RequireAuthorization();

        // Autorização declarada aqui, no endpoint. Os handlers não verificam
        // permissões: se o pedido chega ao handler, já está autorizado.
        group.MapGet("/users", ListUsersAsync)
            .RequireAuthorization(Permissions.UsersRead);

        group.MapGet("/roles", ListRoles)
            .RequireAuthorization(Permissions.RolesRead);

        group.MapPost("/users/{userId:guid}/roles", AssignRoleAsync)
            .RequireAuthorization(Permissions.RolesAssign);

        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        ListUsers listUsers,
        CancellationToken cancellationToken) =>
        Results.Ok(await listUsers.ExecuteAsync(cancellationToken));

    private static IResult ListRoles(ListAccessProfiles listProfiles) =>
        Results.Ok(listProfiles.Execute());

    private static async Task<IResult> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        AssignAccessProfile assignProfile,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await assignProfile.ExecuteAsync(
            userId, request.Profile, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AssignProfileOutcome.Assigned => Results.NoContent(),

            // 404 porque o utilizador é o recurso que o URI identifica.
            AssignProfileOutcome.UserNotFound => Results.NotFound(new { erro = "Utilizador não encontrado." }),

            // 400 e não 404: o perfil vem do corpo, e o recurso que o URI
            // identifica — o utilizador — existe. Um 404 aqui manda procurar o
            // defeito no `userId`, que é o sítio errado. Os perfis válidos
            // seguem na resposta para que o erro se corrija sem consultar
            // outra rota, como já acontece no registo.
            AssignProfileOutcome.ProfileNotFound => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["profile"] =
                    [
                        $"'{request.Profile}' não é um Perfil de Acesso. " +
                        $"Válidos: {string.Join(", ", AccessProfiles.Catalogue.Keys)}.",
                    ],
                }),

            _ => Results.Problem("Resultado inesperado ao atribuir o perfil."),
        };
    }

    /// <summary>
    /// Constrói o contexto de auditoria a partir do pedido. É a camada API que
    /// conhece o transporte — o actor, o endereço de origem e o identificador
    /// de correlação não existem mais abaixo.
    /// </summary>
    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        RegisterUser registerUser,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await registerUser.ExecuteAsync(
            request.Email, request.Password, BuildAuditContext(http), cancellationToken);

        // Password fraca ou e-mail duplicado são violações de regra, não falhas
        // técnicas: devolvem-se ao chamador para que ele possa corrigir.
        return result.Succeeded
            ? Results.Created($"/identity/users/{result.UserId}", new { userId = result.UserId })
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["registo"] = [.. result.Errors],
            });
    }

    private static async Task<IResult> LogInAsync(
        LoginRequest request,
        LogIn logIn,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // O IP e o user agent vivem no transporte; é a camada API que os conhece
        // e os passa ao caso de uso, que os regista na sessão para auditoria.
        var ipAddress = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = http.Request.Headers.UserAgent.ToString();

        var result = await logIn.ExecuteAsync(
            request.Email,
            request.Password,
            ipAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            http.TraceIdentifier,
            cancellationToken);

        // 401 sem detalhe: não revelar se o endereço existe.
        return result.Succeeded
            ? Results.Ok(new LoginResponse(result.AccessToken!, result.ExpiresAt!.Value))
            : Results.Unauthorized();
    }

    /// <summary>
    /// Entrada por Google (ADR-032). O corpo traz o ID token que o frontend
    /// obteve junto da Google; o servidor valida-o e, se corresponder a uma
    /// conta existente, devolve o mesmo <see cref="LoginResponse"/> do login
    /// por password — mesmo token, mesma sessão revogável.
    /// </summary>
    private static async Task<IResult> LogInWithGoogleAsync(
        GoogleLoginRequest request,
        LogInWithGoogle logIn,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var ipAddress = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = http.Request.Headers.UserAgent.ToString();

        var result = await logIn.ExecuteAsync(
            request.IdToken,
            ipAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            http.TraceIdentifier,
            cancellationToken);

        return result.Outcome switch
        {
            GoogleLogInOutcome.Succeeded =>
                Results.Ok(new LoginResponse(result.AccessToken!, result.ExpiresAt!.Value)),

            // 501 e não 401: neste ambiente o Google não está ligado de todo.
            // Um 401 mandaria procurar o defeito na conta de quem tentou, que
            // é o sítio errado — mesma lógica do 501 de `hr` (ADR-015).
            GoogleLogInOutcome.NotConfigured =>
                Results.Problem(
                    "Autenticação com Google não está configurada neste ambiente.",
                    statusCode: StatusCodes.Status501NotImplemented),

            // 401 sem detalhe: não revelar se o token era inválido, se o
            // e-mail não estava verificado ou se não existe conta.
            _ => Results.Unauthorized(),
        };
    }

    private static async Task<IResult> LogOutAsync(
        LogOut logOut,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var sessionId = ReadSessionId(http.User);

        if (sessionId is null)
        {
            return Results.Unauthorized();
        }

        await logOut.ExecuteAsync(sessionId.Value, BuildAuditContext(http), cancellationToken);

        return Results.NoContent();
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? string.Empty;

        if (!Guid.TryParse(userId, out var id))
        {
            return Results.Unauthorized();
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();

        // Devolver as permissões permite ao cliente esconder o que o utilizador
        // não pode fazer. É conveniência de interface — a decisão que conta é a
        // do servidor, não a do cliente.
        var permissions = principal.FindAll(Permissions.ClaimType).Select(claim => claim.Value).ToArray();

        return Results.Ok(new CurrentUserResponse(id, email, roles, permissions));
    }

    /// <summary>
    /// Lê o identificador da sessão do token. O handler do JWT reescreve "sid"
    /// para o URI de <see cref="ClaimTypes.Sid"/>, por isso tentam-se ambos.
    /// </summary>
    private static Guid? ReadSessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.Sid)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sid);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
