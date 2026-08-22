using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.Audit.Api;
using Rivo.Documents.Api;
using Rivo.Hr.Api;
using Rivo.Identity.Api;
using Rivo.Notifications.Api;

namespace Rivo.Architecture.Tests;

/// <summary>
/// Todo o endpoint declara autorização, ou é anónimo de forma explícita.
///
/// <para>
/// Fecha o risco que o ADR-018 §Risks assinala como o mais perigoso do modelo
/// de Minimal APIs: <strong>um `MapPost` sem `RequireAuthorization` fica
/// público em silêncio</strong>. Não falha, não avisa, não aparece em revisão
/// de código a não ser que alguém repare na ausência de uma linha.
/// </para>
///
/// <para>
/// É o anti-padrão A8 do protótipo com outra roupa: lá, a política de escrita
/// era "qualquer membro autenticado" e a verificação real vivia no frontend.
/// </para>
/// </summary>
public class EndpointAuthorizationTests
{
    /// <summary>
    /// Endpoints deliberadamente públicos. Acrescentar aqui é uma decisão
    /// consciente, e é esse o ponto: a lista obriga a que abrir um endpoint ao
    /// mundo seja uma alteração visível a este ficheiro, em vez da ausência
    /// silenciosa de uma linha noutro.
    /// </summary>
    private static readonly HashSet<string> PublicosPorDesenho = new(StringComparer.Ordinal)
    {
        // Autenticar não pode exigir autenticação.
        "POST /identity/login",

        // O mesmo, pelo caminho federado (ADR-032). Público não quer dizer
        // sem verificação: o corpo é um ID token que só serve se a assinatura
        // conferir com as chaves da Google, e só entra em contas que já
        // existem — o Google não cria contas.
        "POST /identity/login/google",

        // Registo de conta. A criação de utilizadores com perfil continua a
        // exigir permissão — ver `POST /identity/users/{userId}/roles`.
        "POST /identity/register",

        // Sonda de disponibilidade. Não revela dados: apenas se a aplicação
        // está viva e se alcança a base de dados.
        "GET /health",
    };

    [Fact]
    public void EveryEndpoint_DeclaresAuthorizationOrIsExplicitlyPublic()
    {
        var desprotegidos = Endpoints()
            .Where(e => !e.RequiresAuthorization)
            .Select(e => e.Route)
            .Where(route => !PublicosPorDesenho.Contains(route))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(desprotegidos);
    }

    /// <summary>
    /// A lista de excepções não cria entradas mortas.
    ///
    /// <para>
    /// Uma excepção que já não corresponde a nenhum endpoint é dívida: dá a
    /// impressão de que alguém decidiu abrir aquela rota, quando a rota
    /// desapareceu ou mudou de nome. Pior, esconde que a lista deixou de ser
    /// revista.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDeclaredPublicEndpoint_StillExists()
    {
        var existentes = Endpoints().Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        var mortas = PublicosPorDesenho
            .Where(route => !existentes.Contains(route))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(mortas);
    }

    /// <summary>
    /// A descoberta não pode ficar vazia — senão os testes acima passavam por
    /// vacuidade, que num teste de segurança é o pior modo de falha.
    /// </summary>
    [Fact]
    public void EndpointDiscovery_FindsTheMappedSurface()
    {
        var rotas = Endpoints().Select(e => e.Route).ToList();

        Assert.NotEmpty(rotas);
        Assert.Contains("POST /identity/login", rotas);
        Assert.Contains("GET /audit/entries", rotas);
    }

    /// <summary>
    /// Mapeia os módulos exactamente como o host faz e lê os metadados
    /// resultantes.
    ///
    /// <para>
    /// Não sobe a aplicação: os endpoints e a sua autorização ficam registados
    /// no momento do mapeamento, antes de haver serviços, base de dados ou
    /// pedidos. É o que torna esta verificação um teste de arquitectura e não
    /// um teste de integração.
    /// </para>
    /// </summary>
    private static IReadOnlyList<(string Route, bool RequiresAuthorization)> Endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        // Os Minimal APIs recusam-se a construir um endpoint cujos parâmetros
        // não consigam classificar, e para isso perguntam ao contentor o que
        // está registado. Chamar os `AddXModule` a sério traria connection
        // strings, chaves de JWT e caminhos de armazenamento para dentro de um
        // teste de arquitectura — e faria falhar por configuração um teste que
        // é sobre autorização.
        //
        // Basta que o descritor exista: a inferência olha para o registo, e a
        // fábrica nunca chega a ser invocada porque nenhum pedido é servido.
        foreach (var type in InjectableTypes())
        {
            builder.Services.AddTransient(type, _ =>
                throw new InvalidOperationException(
                    "Registo apenas para inferência de parâmetros; não deve ser resolvido."));
        }

        // `TimeProvider` vem do BCL, logo não é apanhado pela varredura acima,
        // e vários handlers recebem-no para obter a data corrente.
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();

        app.MapIdentityModule();
        app.MapAuditModule();
        app.MapDocumentsModule();
        app.MapHrModule();
        app.MapNotificationsModule();

        // Espelha o `/health` que o host declara directamente, para que a
        // superfície verificada seja a superfície real.
        app.MapGet("/health", () => Results.Ok());

        // As `DataSources` do próprio builder, e não o `EndpointDataSource` do
        // contentor: este último é um composto que só é povoado quando a
        // aplicação arranca, e aqui nada arranca de propósito.
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints);

        return
        [
            .. endpoints
                .OfType<RouteEndpoint>()
                .Select(endpoint => (
                    Route: Describe(endpoint),
                    RequiresAuthorization:
                        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                        && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        ];
    }

    /// <summary>
    /// Tipos das camadas `Application` e `Contracts` — os casos de uso e os
    /// contratos que os handlers recebem por injecção. Ver <see cref="Endpoints"/>.
    /// </summary>
    private static IEnumerable<Type> InjectableTypes() =>
        RivoAssemblies.All
            .Where(a => RivoAssemblies.Layer(a) is RivoAssemblies.ApplicationLayer
                                                or RivoAssemblies.ContractsLayer)
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !t.IsGenericTypeDefinition)
            // Classes estáticas não podem ser tipo de serviço.
            .Where(t => !(t.IsAbstract && t.IsSealed));

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata
            .GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];

        var verb = methods.Count > 0 ? string.Join("|", methods) : "ANY";

        return $"{verb} /{endpoint.RoutePattern.RawText?.TrimStart('/')}";
    }
}
