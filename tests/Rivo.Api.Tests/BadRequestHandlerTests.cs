using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Api.Errors;

namespace Rivo.Api.Tests;

/// <summary>
/// Um corpo malformado é <c>400</c>, não <c>500</c>.
///
/// <para>
/// Estes testes existem por causa de uma regressão real: registar
/// <c>UseExceptionHandler</c> para o ADR-035 pôs-se à frente do Kestrel, que
/// era quem reconhecia a <see cref="BadHttpRequestException"/>, e todo o JSON
/// inválido passou a sair como <c>500</c>.
/// </para>
/// </summary>
public class BadRequestHandlerTests
{
    [Fact]
    public async Task CorpoMalformado_DevolveOCodigoDaExcepcao()
    {
        var (contexto, corpo) = NovoContexto();

        var tratada = await new BadRequestHandler().TryHandleAsync(
            contexto,
            new BadHttpRequestException("Failed to read parameter from the request body as JSON.", 400),
            CancellationToken.None);

        Assert.True(tratada);
        Assert.Equal(StatusCodes.Status400BadRequest, contexto.Response.StatusCode);

        using var documento = JsonDocument.Parse(await LerAsync(corpo));
        Assert.Equal(400, documento.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Nem toda a <see cref="BadHttpRequestException"/> é 400 — um corpo acima
    /// do limite é 413. O código vem da excepção, não de uma constante.
    /// </summary>
    [Fact]
    public async Task CodigoVemDaExcepcao_NaoEFixoEm400()
    {
        var (contexto, _) = NovoContexto();

        await new BadRequestHandler().TryHandleAsync(
            contexto,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, contexto.Response.StatusCode);
    }

    /// <summary>
    /// A mensagem do desserializador nomeia o tipo .NET e a posição no fluxo de
    /// bytes — informação sobre a implementação, não sobre o pedido.
    /// </summary>
    [Fact]
    public async Task Resposta_NaoRevelaDetalheInterno()
    {
        var (contexto, corpo) = NovoContexto();
        const string interno = "The JSON value could not be converted to Rivo.Fiscal.Api.IntroduceRateRequest.";

        await new BadRequestHandler().TryHandleAsync(
            contexto, new BadHttpRequestException(interno, 400), CancellationToken.None);

        var texto = await LerAsync(corpo);

        Assert.DoesNotContain("Rivo.Fiscal.Api", texto);
        Assert.DoesNotContain("IntroduceRateRequest", texto);
    }

    /// <summary>
    /// Os dois handlers não se pisam: cada um deixa passar o que é do outro, e
    /// a ordem de registo deixa de importar.
    /// </summary>
    [Fact]
    public async Task ColisaoDeConcorrencia_NaoEApanhadaPorEste()
    {
        var (contexto, _) = NovoContexto();

        var tratada = await new BadRequestHandler().TryHandleAsync(
            contexto, new DbUpdateConcurrencyException("colisão"), CancellationToken.None);

        Assert.False(tratada);
        Assert.Equal(StatusCodes.Status200OK, contexto.Response.StatusCode);
    }

    private static (DefaultHttpContext Contexto, MemoryStream Corpo) NovoContexto()
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging();
        servicos.AddProblemDetails();

        var corpo = new MemoryStream();
        var contexto = new DefaultHttpContext
        {
            RequestServices = servicos.BuildServiceProvider(),
        };

        contexto.Request.Method = "POST";
        contexto.Request.Path = "/commercial/customers";
        contexto.Response.Body = corpo;

        return (contexto, corpo);
    }

    private static async Task<string> LerAsync(MemoryStream corpo)
    {
        corpo.Position = 0;
        return await new StreamReader(corpo).ReadToEndAsync();
    }
}
