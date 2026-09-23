using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Rivo.Api.OpenApi;
using Rivo.Fiscal.Contracts;

namespace Rivo.Api.Tests;

/// <summary>
/// Um enum sem <c>JsonStringEnumConverter</c> sai do <c>System.Text.Json</c>
/// como o inteiro subjacente — sem isto, o OpenAPI descrevia-o só como
/// <c>type: integer</c>, sem dizer que <c>1</c> é
/// <see cref="TaxKind.EmployeeSocialSecurity"/> (item #4 do levantamento de
/// pendências).
/// </summary>
public class EnumValuesSchemaTransformerTests
{
    [Fact]
    public async Task Enum_GanhaDescricaoComOsValores()
    {
        var schema = new OpenApiSchema();
        var contexto = NovoContexto(typeof(TaxKind));

        await new EnumValuesSchemaTransformer().TransformAsync(schema, contexto, CancellationToken.None);

        Assert.Contains("0 = ValueAdded", schema.Description);
        Assert.Contains("1 = EmployeeSocialSecurity", schema.Description);
        Assert.Contains("2 = EmployerSocialSecurity", schema.Description);
    }

    [Fact]
    public async Task TipoQueNaoEEnum_NaoEAlterado()
    {
        var schema = new OpenApiSchema { Description = "original" };
        var contexto = NovoContexto(typeof(string));

        await new EnumValuesSchemaTransformer().TransformAsync(schema, contexto, CancellationToken.None);

        Assert.Equal("original", schema.Description);
    }

    private static OpenApiSchemaTransformerContext NovoContexto(Type tipo) =>
        new()
        {
            JsonTypeInfo = JsonSerializerOptions.Default.GetTypeInfo(tipo),
            DocumentName = "v1",
            ParameterDescription = null,
            JsonPropertyInfo = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };
}
