using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Rivo.Api.OpenApi;
using Rivo.Finance.Api;

namespace Rivo.Api.Tests;

/// <summary>
/// <c>LedgerAccountRequest.Category</c> é validado à mão contra
/// <c>AccountCategory</c> (GR/GA/GM/AR/AA/AM) — sem <c>[AllowedValues]</c>, o
/// OpenAPI descrevia o campo como uma string qualquer (item #9 do
/// levantamento de pendências).
/// </summary>
public class AllowedValuesSchemaTransformerTests
{
    [Fact]
    public async Task CampoComAllowedValues_GanhaOEnumNoSchema()
    {
        var schema = new OpenApiSchema();
        var contexto = NovoContexto(typeof(LedgerAccountRequest), nameof(LedgerAccountRequest.Category));

        await new AllowedValuesSchemaTransformer().TransformAsync(schema, contexto, CancellationToken.None);

        Assert.NotNull(schema.Enum);
        Assert.Equal(["GR", "GA", "GM", "AR", "AA", "AM"], schema.Enum!.Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task CampoSemAllowedValues_NaoEAlterado()
    {
        var schema = new OpenApiSchema();
        var contexto = NovoContexto(typeof(LedgerAccountRequest), nameof(LedgerAccountRequest.Code));

        await new AllowedValuesSchemaTransformer().TransformAsync(schema, contexto, CancellationToken.None);

        Assert.Null(schema.Enum);
    }

    private static OpenApiSchemaTransformerContext NovoContexto(Type tipo, string nomeDaPropriedade)
    {
        var tipoInfo = JsonSerializerOptions.Default.GetTypeInfo(tipo);
        var propriedade = tipoInfo.Properties.Single(p =>
            p.AttributeProvider is System.Reflection.PropertyInfo info && info.Name == nomeDaPropriedade);

        return new OpenApiSchemaTransformerContext
        {
            JsonTypeInfo = tipoInfo,
            JsonPropertyInfo = propriedade,
            DocumentName = "v1",
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };
    }
}
