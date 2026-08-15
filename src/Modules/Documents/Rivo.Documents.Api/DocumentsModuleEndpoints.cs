using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Documents.Application;
using Rivo.Documents.Contracts;

namespace Rivo.Documents.Api;

public static class DocumentsModuleEndpoints
{
    /// <summary>Tecto de tamanho por ficheiro, para não esgotar memória nem disco.</summary>
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapDocumentsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/documents");

        group.MapPost("/", UploadAsync)
            .RequireAuthorization(DocumentPermissions.Write)
            // Autenticação é por bearer token, não por cookie, logo não há
            // vector de CSRF que a antiforgery protegesse.
            .DisableAntiforgery();

        group.MapGet("/{documentId:guid}", DownloadAsync)
            .RequireAuthorization(DocumentPermissions.Read);

        group.MapGet("/{documentId:guid}/metadata", GetMetadataAsync)
            .RequireAuthorization(DocumentPermissions.Read);

        return endpoints;
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }

    /// <param name="category">
    /// <c>[FromForm]</c> é obrigatório: sem ele, os minimal APIs ligam tipos
    /// simples à query string, e o campo do formulário seria ignorado.
    /// </param>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        [FromForm] string category,
        UploadDocument upload,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { erro = "Ficheiro vazio." });
        }

        if (file.Length > MaxUploadBytes)
        {
            return Results.BadRequest(new
            {
                erro = $"Ficheiro excede o limite de {MaxUploadBytes / (1024 * 1024)} MB.",
            });
        }

        await using var stream = file.OpenReadStream();

        var descriptor = await upload.ExecuteAsync(
            file.FileName,
            file.ContentType,
            category,
            stream,
            BuildAuditContext(http),
            cancellationToken);

        return Results.Created($"/documents/{descriptor.DocumentId}", descriptor);
    }

    private static async Task<IResult> DownloadAsync(
        Guid documentId,
        DownloadDocument download,
        CancellationToken cancellationToken)
    {
        var content = await download.ExecuteAsync(documentId, cancellationToken);

        return content is null
            ? Results.NotFound()
            : Results.File(content.Content, content.ContentType, content.FileName);
    }

    private static async Task<IResult> GetMetadataAsync(
        Guid documentId,
        IDocumentCatalogue catalogue,
        CancellationToken cancellationToken)
    {
        var descriptor = await catalogue.FindAsync(documentId, cancellationToken);

        return descriptor is null ? Results.NotFound() : Results.Ok(descriptor);
    }
}
