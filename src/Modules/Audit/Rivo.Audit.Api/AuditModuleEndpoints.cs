using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Application;
using Rivo.Audit.Contracts;

namespace Rivo.Audit.Api;

public static class AuditModuleEndpoints
{
    public static IEndpointRouteBuilder MapAuditModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/audit");

        group.MapGet("/entries", QueryAsync)
            .RequireAuthorization(AuditPermissions.TrailRead);

        return endpoints;
    }

    /// <summary>
    /// Só leitura. Não há endpoint de escrita: a trilha é escrita pelos
    /// módulos através do contrato interno, nunca por HTTP — um endpoint
    /// público permitiria forjar registos de auditoria.
    /// </summary>
    private static async Task<IResult> QueryAsync(
        QueryAuditTrail query,
        CancellationToken cancellationToken,
        string? entityType = null,
        string? entityId = null,
        int limit = 50) =>
        Results.Ok(await query.ExecuteAsync(entityType, entityId, limit, cancellationToken));
}
