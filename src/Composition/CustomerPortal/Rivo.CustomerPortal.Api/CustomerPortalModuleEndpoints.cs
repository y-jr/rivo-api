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
}
