using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rivo.Api.Errors;

namespace Rivo.Api.Tests;

/// <summary>
/// Fecha o K15: uma colisão de concorrência optimista é conflito de estado,
/// não falha do servidor (ADR-035).
/// </summary>
public class ConcurrencyConflictHandlerTests
{
    [Fact]
    public async Task ColisaoDeConcorrencia_DevolveConflict()
    {
        var (contexto, corpo) = NovoContexto();

        var tratada = await Handler().TryHandleAsync(
            contexto,
            new DbUpdateConcurrencyException("A instrução afectou 0 linhas."),
            CancellationToken.None);

        Assert.True(tratada);
        Assert.Equal(StatusCodes.Status409Conflict, contexto.Response.StatusCode);
        Assert.Contains("application/problem+json", contexto.Response.ContentType);

        // O que o cliente precisa de saber para agir: releia e repita.
        Assert.Contains("Recarregue", await LerAsync(corpo));
    }

    /// <summary>
    /// A mensagem do EF Core nomeia tabela e tipo de entidade. Devolvê-la ao
    /// cliente contraria `standards/error-handling.md` — detalhe interno não
    /// sai na resposta, sai no log.
    /// </summary>
    [Fact]
    public async Task Resposta_NaoRevelaDetalheInterno()
    {
        var (contexto, corpo) = NovoContexto();
        const string interno = "app_user_role: a instrução UPDATE afectou 0 das 1 linhas esperadas";

        await Handler().TryHandleAsync(
            contexto,
            new DbUpdateConcurrencyException(interno),
            CancellationToken.None);

        var texto = await LerAsync(corpo);

        Assert.DoesNotContain("app_user_role", texto);
        Assert.DoesNotContain("UPDATE", texto);
    }

    /// <summary>
    /// Só a concorrência. Alargar a <see cref="DbUpdateException"/> esconderia
    /// violações de chave e de restrição, que são defeitos e devem falhar
    /// ruidosamente — o handler tem de as deixar passar.
    /// </summary>
    [Theory]
    [InlineData(typeof(DbUpdateException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task OutrasExcepcoes_NaoSaoTratadas(Type tipo)
    {
        var (contexto, _) = NovoContexto();
        var excepcao = (Exception)Activator.CreateInstance(tipo)!;

        var tratada = await Handler().TryHandleAsync(
            contexto, excepcao, CancellationToken.None);

        Assert.False(tratada);

        // Sem tocar na resposta: quem trata a seguir tem de a encontrar intacta.
        Assert.Equal(StatusCodes.Status200OK, contexto.Response.StatusCode);
    }

    [Fact]
    public async Task Resposta_EhProblemDetailsValido()
    {
        var (contexto, corpo) = NovoContexto();

        await Handler().TryHandleAsync(
            contexto,
            new DbUpdateConcurrencyException("colisão"),
            CancellationToken.None);

        using var documento = JsonDocument.Parse(await LerAsync(corpo));

        Assert.Equal(409, documento.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(documento.RootElement.GetProperty("title").GetString()));
    }

    private static ConcurrencyConflictHandler Handler() =>
        new(NullLogger<ConcurrencyConflictHandler>.Instance);

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
        contexto.Request.Path = "/approval/requests/x/decisions";
        contexto.Response.Body = corpo;

        return (contexto, corpo);
    }

    private static async Task<string> LerAsync(MemoryStream corpo)
    {
        corpo.Position = 0;
        return await new StreamReader(corpo).ReadToEndAsync();
    }
}
