using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.EmployeePortal.Application;

namespace Rivo.EmployeePortal.Api;

public static class EmployeePortalModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso. Vive aqui — em `Api`, não em `Infrastructure`
    /// — porque a camada de composição não tem uma (ADR-041).
    /// </summary>
    public static IServiceCollection AddEmployeePortalModule(this IServiceCollection services)
    {
        services.AddScoped<GetMyProfile>();

        return services;
    }

    public static IEndpointRouteBuilder MapEmployeePortalModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/portal");

        // Sem permissão nenhuma — só autenticação. "Próprio" não é uma
        // operação que se atribui a um perfil (ADR-042); é consequência de
        // estar autenticado, seja qual for o perfil. Nunca aceita
        // `employeeId`: devolve sempre e só o colaborador do próprio
        // chamador — para ver dados de terceiros, os endpoints de `hr` com
        // `hr.employees.read` continuam a ser o caminho.
        group.MapGet("/me", GetMyProfileAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetMyProfileAsync(
        HttpContext http,
        GetMyProfile getMyProfile,
        TimeProvider clock,
        CancellationToken cancellationToken)
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

        var result = await getMyProfile.ExecuteAsync(userId, clock.GetUtcNow(), cancellationToken);

        return result.Outcome switch
        {
            MyProfileOutcome.Found => Results.Ok(result.Profile),
            MyProfileOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum colaborador.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }
}
