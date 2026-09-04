using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Approval.Contracts;
using Rivo.Commercial.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Identity.Contracts;
using Rivo.Procurement.Contracts;
using Rivo.Settings.Application;

namespace Rivo.Settings.Api;

public static class SettingsModuleEndpoints
{
    /// <summary>Mesmo tecto de `Rivo.Documents.Api` — uma folha de importação não precisa de mais do que isto.</summary>
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Regista o caso de uso. Vive aqui — em `Api`, não em `Infrastructure` —
    /// porque a camada de composição não tem uma: sem base de dados, sem
    /// connection string, nada para configurar além do próprio registo
    /// (ADR-041).
    /// </summary>
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<GetAdministrationOverview>();
        services.AddScoped<ImportCustomersFromCsv>();
        services.AddScoped<ImportEmployeesFromCsv>();
        services.AddScoped<ImportSuppliersFromCsv>();

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

        // Importação em massa via CSV (ADR-047): cada uma atrás da mesma
        // permissão de escrita que já protege o formulário normal da
        // entidade — sem permissão nova, a audiência é a mesma.
        group.MapPost("/import/customers", ImportCustomersAsync)
            .RequireAuthorization(CommercialPermissions.CustomersWrite)
            .DisableAntiforgery();

        group.MapPost("/import/employees", ImportEmployeesAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .DisableAntiforgery();

        group.MapPost("/import/suppliers", ImportSuppliersAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersWrite)
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        GetAdministrationOverview getOverview,
        CancellationToken cancellationToken) =>
        Results.Ok(await getOverview.ExecuteAsync(cancellationToken));

    private static Task<IResult> ImportCustomersAsync(
        IFormFile file, ImportCustomersFromCsv import, HttpContext http, CancellationToken cancellationToken) =>
        RunImportAsync(file, http, cancellationToken, (content, actorId, ct) => import.ExecuteAsync(content, actorId, ct));

    private static Task<IResult> ImportEmployeesAsync(
        IFormFile file, ImportEmployeesFromCsv import, HttpContext http, CancellationToken cancellationToken) =>
        RunImportAsync(file, http, cancellationToken, (content, actorId, ct) => import.ExecuteAsync(content, actorId, ct));

    private static Task<IResult> ImportSuppliersAsync(
        IFormFile file, ImportSuppliersFromCsv import, HttpContext http, CancellationToken cancellationToken) =>
        RunImportAsync(file, http, cancellationToken, (content, actorId, ct) => import.ExecuteAsync(content, actorId, ct));

    private static async Task<IResult> RunImportAsync(
        IFormFile file,
        HttpContext http,
        CancellationToken cancellationToken,
        Func<string, Guid, CancellationToken, Task<CsvImportResult>> import)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { erro = "Ficheiro vazio." });
        }

        if (file.Length > MaxUploadBytes)
        {
            return Results.BadRequest(new { erro = $"Ficheiro excede o limite de {MaxUploadBytes / (1024 * 1024)} MB." });
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(cancellationToken);

        var result = await import(content, ResolveActorId(http), cancellationToken);

        return result.Outcome switch
        {
            CsvImportOutcome.Imported => Results.Ok(result.Summary),
            _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["ficheiro"] = [result.Error!] }),
        };
    }

    /// <summary>Mesma extracção de `Rivo.Documents.Api.DocumentsModuleEndpoints.BuildAuditContext` — quem importou, para a trilha de auditoria de cada registo criado.</summary>
    private static Guid ResolveActorId(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(actor, out var id) ? id : Guid.Empty;
    }
}
