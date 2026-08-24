using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Fiscal.Application.UseCases;
using Rivo.Fiscal.Contracts;

namespace Rivo.Fiscal.Api;

public static class FiscalModuleEndpoints
{
    public static IEndpointRouteBuilder MapFiscalModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/fiscal");

        group.MapGet("/tax-rates", ListAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        // Escrita de taxa é configuração sensível: altera o valor de todas as
        // facturas emitidas a partir da data escolhida (ADR-011 §6).
        group.MapPost("/tax-rates", OpenScheduleAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        group.MapPost("/tax-rates/{scheduleId:guid}/versions", IntroduceAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        // Determinação: o que `commercial` e `finance` fazem por contrato, aqui
        // exposto para se poder conferir o que a emissão vai receber.
        group.MapGet("/tax-rates/determination", DetermineAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListTaxRates listRates,
        CancellationToken cancellationToken) =>
        Results.Ok(await listRates.ExecuteAsync(cancellationToken));

    private static async Task<IResult> OpenScheduleAsync(
        OpenScheduleRequest request,
        OpenTaxRateSchedule openSchedule,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openSchedule.ExecuteAsync(
            request.Kind ?? TaxKind.ValueAdded,
            request.Code,
            request.Description,
            BuildAuditContext(http),
            cancellationToken);

        return result.Succeeded
            ? Results.Created($"/fiscal/tax-rates/{result.ScheduleId}", new { scheduleId = result.ScheduleId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["taxa"] = [result.Error!] });
    }

    private static async Task<IResult> IntroduceAsync(
        Guid scheduleId,
        IntroduceRateRequest request,
        IntroduceTaxRate introduceRate,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await introduceRate.ExecuteAsync(
            scheduleId,
            request.Percentage,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.LegalInstrument,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            IntroduceRateOutcome.Introduced =>
                Results.Created($"/fiscal/tax-rates/{scheduleId}", new { versionId = result.VersionId }),

            IntroduceRateOutcome.ScheduleNotFound =>
                Results.NotFound(new { erro = "Série de taxa não encontrada." }),

            // 409 e não 400: a sobreposição não é um campo mal preenchido, é
            // conflito com o que já lá está. Quem chama corrige fechando a
            // versão anterior, não reescrevendo o pedido.
            IntroduceRateOutcome.Rejected =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem("Resultado inesperado ao introduzir a taxa."),
        };
    }

    private static async Task<IResult> DetermineAsync(
        ITaxDetermination determination,
        string taxCode,
        DateOnly taxPointDate,
        TaxKind? kind,
        CancellationToken cancellationToken)
    {
        var result = await determination.DetermineAsync(
            new TaxDeterminationRequest(kind ?? TaxKind.ValueAdded, taxCode, taxPointDate),
            cancellationToken);

        return result.Outcome switch
        {
            TaxDeterminationOutcome.Determined => Results.Ok(result.Determination),

            // 404: não há regra que cubra esta data. Recusar é a resposta certa
            // — recair na versão mais próxima inventaria o valor.
            TaxDeterminationOutcome.NoRateInForce =>
                Results.NotFound(new { erro = "Não há taxa em vigor para este código à data indicada." }),

            // 501: a capacidade não existe neste sistema, e não é defeito do
            // pedido. O catálogo de códigos de isenção está adiado pelo
            // ADR-036, e não se inventa código.
            TaxDeterminationOutcome.ExemptionCodeUnavailable =>
                Results.Problem(
                    "Emitir com isenção exige o catálogo de códigos de isenção, que ainda não existe (ADR-036).",
                    statusCode: StatusCodes.Status501NotImplemented),

            _ => Results.Problem("Resultado inesperado na determinação."),
        };
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }
}

public sealed record OpenScheduleRequest(TaxKind? Kind, string Code, string Description);

public sealed record IntroduceRateRequest(
    decimal Percentage,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string LegalInstrument);
