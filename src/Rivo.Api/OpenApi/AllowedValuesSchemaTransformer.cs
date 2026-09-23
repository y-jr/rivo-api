using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rivo.Api.OpenApi;

/// <summary>
/// Documenta os valores aceites de um campo <c>string</c> validado à mão
/// contra um conjunto fixo — o caso de <c>LedgerAccountRequest.Category</c>
/// (GR/GA/GM/AR/AA/AM, item #9 do levantamento de pendências) e, mais tarde,
/// de qualquer campo semelhante (ex. o tipo de manutenção da Frota).
///
/// <para>
/// O campo continua <c>string</c> — a validação real é o
/// <c>Enum.TryParse</c> que já existe no handler. `[AllowedValues]`
/// (`System.ComponentModel.DataAnnotations`) só marca, para quem lê o
/// contrato, que valores esse `Enum.TryParse` de facto aceita — sem essa
/// marca, o OpenAPI descrevia o campo como uma string qualquer.
/// </para>
/// </summary>
public sealed class AllowedValuesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var atributo = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(AllowedValuesAttribute), inherit: true)
            .OfType<AllowedValuesAttribute>()
            .FirstOrDefault();

        if (atributo is null)
        {
            return Task.CompletedTask;
        }

        schema.Enum = [.. atributo.Values
            .Where(valor => valor is not null)
            .Select(valor => JsonValue.Create(valor.ToString()) as JsonNode)];

        return Task.CompletedTask;
    }
}
