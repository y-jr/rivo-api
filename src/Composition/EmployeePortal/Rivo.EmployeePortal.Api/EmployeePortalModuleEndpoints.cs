using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.EmployeePortal.Application;

namespace Rivo.EmployeePortal.Api;

public static class EmployeePortalModuleEndpoints
{
    /// <summary>
    /// Regista o caso de uso. Vive aqui — em `Api`, não em `Infrastructure`
    /// — porque a camada de composição não tem uma (ADR-041).
    /// </summary>
    public static IServiceCollection AddEmployeePortalModule(this IServiceCollection services)
    {
        services.AddScoped<GetMyProfile>();
        services.AddScoped<GetMyAttendance>();
        services.AddScoped<GetMyLeave>();
        services.AddScoped<GetMyDocuments>();

        return services;
    }

    public static IEndpointRouteBuilder MapEmployeePortalModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/portal");

        // Sem permissão nenhuma — só autenticação. "Próprio" não é uma
        // operação que se atribui a um perfil (ADR-042); é consequência de
        // estar autenticado, seja qual for o perfil. Nunca aceita
        // `employeeId`: devolve sempre e só o colaborador do próprio
        // chamador — para ver dados de terceiros, os endpoints de `hr` com
        // `hr.employees.read` continuam a ser o caminho.
        group.MapGet("/me", GetMyProfileAsync).RequireAuthorization();

        // As tres leituras do proprio. Sem permissao, pela mesma razao do
        // `/me`: nenhuma delas aceita `employeeId`, e o que devolvem e sempre
        // e so o colaborador de quem chama.
        group.MapGet("/me/attendance", GetMyAttendanceAsync).RequireAuthorization();
        group.MapGet("/me/leave", GetMyLeaveAsync).RequireAuthorization();
        group.MapGet("/me/documents", GetMyDocumentsAsync).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// O identificador da conta que chama, tirado do token.
    ///
    /// <para>
    /// Extraído dos handlers quando passaram de um para quatro: a mesma
    /// leitura repetida quatro vezes é a mesma decisão de segurança tomada
    /// quatro vezes, e basta uma delas divergir para um dos endpoints deixar
    /// de resolver "o próprio" como os outros.
    /// </para>
    ///
    /// <para>
    /// Devolve <c>null</c> quando o token não traz identificador reconhecível
    /// — não deveria acontecer com um token emitido pelo Rivo, mas recusa-se
    /// em vez de adivinhar (ADR-042, "nunca tenta adivinhar").
    /// </para>
    /// </summary>
    private static Guid? QuemChama(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(actor, out var userId) ? userId : null;
    }

    private static IResult SessaoSemIdentificador() =>
        Results.Problem(
            "Sessão sem identificador de utilizador.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult SemVinculo() =>
        Results.Problem(
            "Esta conta não está associada a nenhum colaborador.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Traduz o desfecho das três leituras do próprio.
    ///
    /// <para>
    /// <c>403</c> e não <c>404</c> quando não há vínculo: a conta existe e
    /// está autenticada, só não tem "o próprio" que o portal existe para
    /// mostrar.
    /// </para>
    /// </summary>
    private static IResult Traduzir<T>(MyRecordsResult<T> resultado) =>
        resultado.Outcome switch
        {
            MyRecordsOutcome.Found => Results.Ok(resultado.Records),
            MyRecordsOutcome.NotLinked => SemVinculo(),
            MyRecordsOutcome.Rejected => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["periodo"] = [resultado.Error!] }),
            _ => throw new ArgumentOutOfRangeException(
                nameof(resultado), resultado.Outcome, "Desfecho sem tradução HTTP."),
        };

    private static async Task<IResult> GetMyAttendanceAsync(
        HttpContext http,
        GetMyAttendance getMyAttendance,
        TimeProvider clock,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (QuemChama(http) is not { } userId)
        {
            return SessaoSemIdentificador();
        }

        // Sem janela indicada, o mês corrente. Devolver a assiduidade inteira
        // por omissão faria o pedido crescer com a antiguidade de quem o faz —
        // e um mês é o que se olha para conferir o recibo.
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var inicio = from ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var fim = to ?? hoje;

        var resultado = await getMyAttendance.ExecuteAsync(
            userId, inicio, fim, clock.GetUtcNow(), cancellationToken);

        return Traduzir(resultado);
    }

    private static async Task<IResult> GetMyLeaveAsync(
        HttpContext http,
        GetMyLeave getMyLeave,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (QuemChama(http) is not { } userId)
        {
            return SessaoSemIdentificador();
        }

        return Traduzir(await getMyLeave.ExecuteAsync(userId, clock.GetUtcNow(), cancellationToken));
    }

    private static async Task<IResult> GetMyDocumentsAsync(
        HttpContext http,
        GetMyDocuments getMyDocuments,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (QuemChama(http) is not { } userId)
        {
            return SessaoSemIdentificador();
        }

        return Traduzir(await getMyDocuments.ExecuteAsync(userId, clock.GetUtcNow(), cancellationToken));
    }

    private static async Task<IResult> GetMyProfileAsync(
        HttpContext http,
        GetMyProfile getMyProfile,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (QuemChama(http) is not { } userId)
        {
            return SessaoSemIdentificador();
        }

        var result = await getMyProfile.ExecuteAsync(userId, clock.GetUtcNow(), cancellationToken);

        return result.Outcome switch
        {
            MyProfileOutcome.Found => Results.Ok(result.Profile),
            MyProfileOutcome.NotLinked => SemVinculo(),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }
}
