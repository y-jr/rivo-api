using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rivo.EmployeePortal.Application;
using Rivo.Hr.Contracts;
using Rivo.Payroll.Contracts;

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
        services.AddScoped<GetMyRecords>();

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
        group.MapGet("/me", GetMyProfileAsync).RequireAuthorization()
            .Produces<MyProfileView>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // As mesmas regras valem para as quatro abaixo: autenticação e mais
        // nada, e nenhuma aceita `employeeId`. O 403 que devolvem é por falta
        // de vínculo, nunca por falta de permissão.
        group.MapGet("/me/attendance", GetMyAttendanceAsync).RequireAuthorization()
            .Produces<IReadOnlyList<OwnAttendanceRecord>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/me/leave", GetMyLeaveAsync).RequireAuthorization()
            .Produces<IReadOnlyList<OwnLeaveRequest>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/me/documents", GetMyDocumentsAsync).RequireAuthorization()
            .Produces<IReadOnlyList<OwnEmployeeDocument>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/me/payslips", GetMyPayslipsAsync).RequireAuthorization()
            .Produces<IReadOnlyList<OwnPayslip>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetMyProfileAsync(
        HttpContext http,
        GetMyProfile getMyProfile,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(actor, out var userId))
        {
            // Autenticado, mas sem identificador reconhecível no token — não
            // deveria acontecer com um token emitido pelo Rivo, mas
            // recusa-se em vez de adivinhar (mesma disciplina de ADR-042).
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await getMyProfile.ExecuteAsync(userId, clock.GetUtcNow(), cancellationToken);

        return result.Outcome switch
        {
            MyProfileOutcome.Found => Results.Ok(result.Profile),
            MyProfileOutcome.NotLinked => Results.Problem(
                "Esta conta não está associada a nenhum colaborador.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }
    /// <summary>
    /// O identificador da conta autenticada, lido do token.
    ///
    /// <para>
    /// Extraído porque passou a ser preciso em cinco sítios. Devolver
    /// <c>null</c> em vez de lançar deixa cada handler traduzir a falha no
    /// mesmo 403 que já usa para a falta de vínculo — do ponto de vista de
    /// quem chama, as duas dizem a mesma coisa: esta sessão não tem um
    /// «próprio» para mostrar.
    /// </para>
    /// </summary>
    private static Guid? QuemChama(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(actor, out var userId) ? userId : null;
    }

    private static IResult SemVinculo() =>
        Results.Problem(
            "Esta conta não está associada a nenhum colaborador.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Traduz um desfecho de <see cref="GetMyRecords"/> em resposta HTTP.
    /// Genérico porque as quatro leituras diferem no tipo e em mais nada.
    /// </summary>
    private static async Task<IResult> ResponderAsync<T>(
        HttpContext http,
        Func<Guid, Task<MyRecordsResult<T>>> ler)
    {
        if (QuemChama(http) is not { } userId)
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.", statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await ler(userId);

        return result.Outcome switch
        {
            MyRecordsOutcome.Found => Results.Ok(result.Records),
            MyRecordsOutcome.NotLinked => SemVinculo(),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    /// <param name="from">Omitido, o primeiro dia do mês corrente.</param>
    /// <param name="to">Omitido, hoje.</param>
    private static Task<IResult> GetMyAttendanceAsync(
        HttpContext http,
        GetMyRecords records,
        TimeProvider clock,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);

        // A janela por omissão é o mês corrente. Sem omissão, quem abrisse o
        // portal sem escolher datas puxava o histórico inteiro pela rede.
        var inicio = from ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var fim = to ?? hoje;

        // Mesma recusa do endpoint de `hr`, e escrita aqui porque a validação
        // de janela vive na camada Api de cada um: sem isto, uma janela
        // invertida devolvia 200 com uma lista vazia, que o ecrã leria como
        // «não tem marcações».
        if (fim < inicio)
        {
            return Task.FromResult(Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["A data final não pode ser anterior à inicial."],
            }));
        }

        return ResponderAsync(http, userId =>
            records.AttendanceAsync(userId, inicio, fim, agora, cancellationToken));
    }

    private static Task<IResult> GetMyLeaveAsync(
        HttpContext http,
        GetMyRecords records,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ResponderAsync(http, userId =>
            records.LeaveAsync(userId, clock.GetUtcNow(), cancellationToken));

    private static Task<IResult> GetMyDocumentsAsync(
        HttpContext http,
        GetMyRecords records,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ResponderAsync(http, userId =>
            records.DocumentsAsync(userId, clock.GetUtcNow(), cancellationToken));

    private static Task<IResult> GetMyPayslipsAsync(
        HttpContext http,
        GetMyRecords records,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ResponderAsync(http, userId =>
            records.PayslipsAsync(userId, clock.GetUtcNow(), cancellationToken));
}
