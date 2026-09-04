using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Analytics.Application;
using Rivo.Analytics.Contracts;

namespace Rivo.Analytics.Api;

public static class AnalyticsModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso e a policy da permissão própria — mesmo padrão
    /// que `Rivo.Dashboard.Api.DashboardModuleEndpoints` já usa, pela mesma
    /// razão (ADR-041): a camada de composição não tem `Infrastructure`.
    /// </summary>
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        services.AddScoped<GetAnalyticsOverview>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in AnalyticsPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim("permission", permission));
            }
        });

        return services;
    }

    public static IEndpointRouteBuilder MapAnalyticsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/analytics");

        group.MapGet("/overview", GetOverviewAsync)
            .RequireAuthorization(AnalyticsPermissions.OverviewRead);

        return endpoints;
    }

    /// <summary>
    /// <c>currency</c> tem omissão — AOA é conveniente, nunca regra de
    /// negócio. <c>from</c>/<c>to</c> não têm: que período mostrar por
    /// omissão é decisão de produto, não se inventa aqui (mesmo raciocínio
    /// do Dashboard Executivo).
    /// </summary>
    private static async Task<IResult> GetOverviewAsync(
        DateOnly from,
        DateOnly to,
        GetAnalyticsOverview getOverview,
        CancellationToken cancellationToken,
        string currency = "AOA")
    {
        var result = await getOverview.ExecuteAsync(from, to, currency, cancellationToken);

        return result.Outcome switch
        {
            AnalyticsOverviewOutcome.Computed => Results.Ok(result.Overview),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["janela"] = [result.Error!] }),
        };
    }
}
