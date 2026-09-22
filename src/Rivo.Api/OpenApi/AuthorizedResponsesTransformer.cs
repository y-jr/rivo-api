using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rivo.Api.OpenApi;

/// <summary>
/// Acrescenta 401 e 403 a qualquer operação que exija autorização — sem isto,
/// cada um dos endpoints protegidos teria de os declarar à mão, e são sempre
/// os mesmos dois: sem sessão, ou com o perfil errado.
///
/// <para>
/// Sem corpo de propósito: nem o desafio JWT nem o <c>AuthorizationMiddleware</c>
/// passam por <c>IProblemDetailsService</c> hoje (não há <c>UseStatusCodePages</c>
/// nem handlers de <c>OnChallenge</c>/<c>OnForbidden</c> registados) — descrever
/// aqui um corpo <c>ProblemDetails</c> seria documentar uma resposta que a API
/// não dá.
/// </para>
/// </summary>
public sealed class AuthorizedResponsesTransformer : IOpenApiOperationTransformer
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

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Sem sessão válida." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Sessão válida, mas sem a permissão exigida." });

        return Task.CompletedTask;
    }
}
