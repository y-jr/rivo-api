using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Rivo.Api.OpenApi;

/// <summary>
/// Documenta os valores de qualquer enum exposto num pedido ou resposta.
///
/// <para>
/// Sem `JsonStringEnumConverter`, o `System.Text.Json` serializa um enum como
/// o inteiro subjacente, e o OpenAPI nativo descreve isso só como
/// `type: integer` — sem dizer que `1` é `EmployeeSocialSecurity`. É o caso de
/// `TaxKind`, `SubsidyKind` e `ResourceKind` (item #4 do levantamento de
/// pendências), mas não só esses — qualquer enum que uma vista devolva
/// directamente (ex. `CustomerStatus`) tem o mesmo problema.
/// </para>
///
/// <para>
/// <strong>Documenta o que já é enviado, não muda o que é enviado.</strong>
/// Mudar o wire format para string quebraria quem já lê inteiros — o
/// frontend actual, entre outros — e essa é uma decisão de compatibilidade
/// que não cabe a uma correcção de documentação.
/// </para>
/// </summary>
public sealed class EnumValuesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var tipo = context.JsonTypeInfo.Type;
        var tipoDoEnum = Nullable.GetUnderlyingType(tipo) ?? tipo;

        if (!tipoDoEnum.IsEnum)
        {
            return Task.CompletedTask;
        }

        var valores = Enum.GetValues(tipoDoEnum)
            .Cast<object>()
            .Select(valor => $"{Convert.ToInt64(valor)} = {valor}");

        var mapa = string.Join(", ", valores);

        schema.Description = string.IsNullOrWhiteSpace(schema.Description)
            ? mapa
            : $"{schema.Description} ({mapa})";

        return Task.CompletedTask;
    }
}
