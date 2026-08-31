using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Approval.Contracts;
using Rivo.Identity.Contracts;
using Rivo.Settings.Application;

namespace Rivo.Settings.Api;

public static class SettingsModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso. Vive aqui — em `Api`, não em `Infrastructure` —
    /// porque a camada de composição não tem uma: sem base de dados, sem
    /// connection string, nada para configurar além do próprio registo
    /// (ADR-041).
    /// </summary>
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<GetAdministrationOverview>();

        return services;
    }

    public static IEndpointRouteBuilder MapSettingsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/settings");

        // As duas permissões, não uma nova: a vista soma o que já existe em
        // `identity` e `approval`, e hoje só `Admin` tem as duas — ver
        // AccessProfiles.Catalogue em `identity`. Inventar uma permissão
        // própria duplicaria essa decisão em vez de a reflectir.
        group.MapGet("/overview", GetOverviewAsync)
            .RequireAuthorization(IdentityPermissions.RolesRead, ApprovalPermissions.PoliciesRead);

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        GetAdministrationOverview getOverview,
        CancellationToken cancellationToken) =>
        Results.Ok(await getOverview.ExecuteAsync(cancellationToken));
}
