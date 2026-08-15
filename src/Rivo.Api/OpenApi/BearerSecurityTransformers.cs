using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rivo.Api.OpenApi;

/// <summary>
/// Declara o esquema "Bearer" no documento OpenAPI.
///
/// Sem isto o Swagger UI não mostra o botão "Authorize", e os endpoints
/// autenticados ficam impossíveis de experimentar a partir da interface.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Cole o token devolvido por /identity/login. O prefixo 'Bearer' é acrescentado automaticamente.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marca como protegidas apenas as operações que exigem autorização.
///
/// Aplicar o requisito globalmente marcaria /identity/register e
/// /identity/login como protegidos, o que seria falso.
/// </summary>
public sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requiresAuthorization = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName)] = [],
            },
        ];

        return Task.CompletedTask;
    }
}
