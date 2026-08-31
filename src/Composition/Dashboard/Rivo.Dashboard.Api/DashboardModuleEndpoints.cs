using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Dashboard.Application;
using Rivo.Dashboard.Contracts;

namespace Rivo.Dashboard.Api;

public static class DashboardModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso e a policy da permissão própria. Vive aqui —
    /// em `Api`, não em `Infrastructure` — porque a camada de composição
    /// não tem uma (ADR-041); mesmo assim é o mesmo registo que qualquer
    /// `AddXModule` faz para as suas permissões (ADR-014).
    ///
    /// <para>
    /// <c>"permission"</c> em vez de <c>IdentityPermissions.ClaimType</c>,
    /// de propósito: evita uma referência a `identity` só por uma
    /// constante — mesmo valor, mesmo padrão que `Rivo.Hr.Infrastructure`
    /// já usa.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        services.AddScoped<GetExecutiveOverview>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in DashboardPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static IEndpointRouteBuilder MapDashboardModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dashboard");

        group.MapGet("/overview", GetOverviewAsync)
            .RequireAuthorization(DashboardPermissions.OverviewRead);

        return endpoints;
    }

    /// <summary>
    /// <c>currency</c> e <c>topCustomers</c> têm omissão — AOA e 5 são
    /// convenientes, nunca uma regra de negócio; quem chama pode pedir
    /// outra coisa. <c>from</c>/<c>to</c> não têm: que período mostrar por
    /// omissão é decisão de produto, não de engenharia, e não se inventa
    /// aqui.
    /// </summary>
    private static async Task<IResult> GetOverviewAsync(
        DateOnly from,
        DateOnly to,
        GetExecutiveOverview getOverview,
        CancellationToken cancellationToken,
        string currency = "AOA",
        int topCustomers = 5)
    {
        var result = await getOverview.ExecuteAsync(from, to, currency, topCustomers, cancellationToken);

        return result.Outcome switch
        {
            ExecutiveOverviewOutcome.Computed => Results.Ok(result.Overview),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["janela"] = [result.Error!] }),
        };
    }
}
