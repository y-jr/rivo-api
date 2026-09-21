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

        // Fecha a versão corrente, para desbloquear a que lhe sucede — a
        // resposta a "feche a anterior primeiro" que a introdução devolve
        // numa sobreposição.
        group.MapPost("/tax-rates/{scheduleId:guid}/versions/{versionId:guid}/closure", CloseVersionAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        // Determinação: o que `commercial` e `finance` fazem por contrato, aqui
        // exposto para se poder conferir o que a emissão vai receber.
        group.MapGet("/tax-rates/determination", DetermineAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        group.MapGet("/income-tax-schedule", GetIncomeTaxScheduleAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        // Escrita de escalões é configuração sensível: altera o IRT de todos
        // os recibos calculados a partir da data escolhida (ADR-011 §5).
        group.MapPost("/income-tax-schedule/versions", IntroduceIncomeTaxScheduleVersionAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        group.MapPost("/income-tax-schedule/versions/{versionId:guid}/closure", CloseIncomeTaxScheduleVersionAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        group.MapGet("/income-tax-schedule/determination", DetermineIncomeTaxAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        group.MapGet("/subsidy-exemptions", GetSubsidyExemptionScheduleAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        // Escrita de limiar é configuração sensível: altera a matéria
        // colectável de IRT de todas as folhas calculadas a partir da data
        // escolhida (ADR-011 §5).
        group.MapPost("/subsidy-exemptions/versions", IntroduceSubsidyExemptionVersionAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        group.MapPost("/subsidy-exemptions/versions/{versionId:guid}/closure", CloseSubsidyExemptionVersionAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        group.MapGet("/subsidy-exemptions/determination", DetermineSubsidyExemptionAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        // --- A identidade fiscal da empresa (ADR-066) ---
        //
        // `GET` e `PUT`, e não `POST`: é um recurso singular que se declara uma
        // vez e se corrige depois, não uma colecção onde se acrescentam
        // registos. A mesma leitura de verbo do ADR-063 — corrigir é `PUT`.
        group.MapGet("/tax-entity", GetTaxEntityAsync)
            .RequireAuthorization(FiscalPermissions.TaxEntityRead);

        group.MapPut("/tax-entity", DeclareTaxEntityAsync)
            .RequireAuthorization(FiscalPermissions.TaxEntityWrite);

        return endpoints;
    }

    /// <summary>
    /// A identidade fiscal, ou <c>404</c> se ainda não houver.
    ///
    /// <para>
    /// <strong>404 e não um objecto vazio.</strong> "A empresa não declarou quem
    /// é" e "a empresa chama-se nada" são estados diferentes, e um corpo com
    /// campos em branco confundia-os. O ecrã de configuração trata o 404 como
    /// "por preencher", que é o que ele é.
    /// </para>
    /// </summary>
    private static async Task<IResult> GetTaxEntityAsync(
        GetTaxEntityProfile query,
        CancellationToken cancellationToken)
    {
        var perfil = await query.ExecuteAsync(cancellationToken);

        return perfil is null
            ? Results.NotFound(new { erro = "A identidade fiscal da empresa ainda não foi declarada." })
            : Results.Ok(perfil);
    }

    private static async Task<IResult> DeclareTaxEntityAsync(
        TaxEntityRequest request,
        DeclareTaxEntityProfile declare,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await declare.ExecuteAsync(
            new TaxEntityProfileInput(
                request.CompanyName,
                request.TaxRegistrationNumber,
                request.AddressDetail,
                request.City,
                request.Country,
                request.BusinessName,
                request.PostalCode,
                request.Email,
                request.Phone,
                request.SoftwareValidationNumber),
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            // 201 na primeira vez, 200 nas seguintes: quem configura o sistema
            // vê a diferença entre ter criado e ter corrigido.
            TaxEntityProfileOutcome.Declared => Results.Created("/fiscal/tax-entity", result.Profile),
            TaxEntityProfileOutcome.Corrected => Results.Ok(result.Profile),
            TaxEntityProfileOutcome.Invalid =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["emitente"] = [result.Error!] }),
            _ => Results.Problem("Resultado inesperado ao declarar a identidade fiscal."),
        };
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
            IntroduceRateOutcome.Overlaps =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            // 400: instrumento legal em branco, taxa fora de 0–100, vigência
            // invertida. Aqui o pedido é que está mal, e corrige-se no pedido.
            IntroduceRateOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["taxa"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao introduzir a taxa."),
        };
    }

    private static async Task<IResult> CloseVersionAsync(
        Guid scheduleId,
        Guid versionId,
        CloseVersionRequest request,
        CloseTaxRateVersion closeVersion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await closeVersion.ExecuteAsync(
            scheduleId, versionId, request.EffectiveTo, BuildAuditContext(http), cancellationToken);

        return CloseVersionResponse(result.Outcome, result.Error);
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

    private static async Task<IResult> GetIncomeTaxScheduleAsync(
        GetIncomeTaxSchedule getSchedule,
        CancellationToken cancellationToken)
    {
        var tabela = await getSchedule.ExecuteAsync(cancellationToken);

        return tabela is null
            ? Results.NotFound(new { erro = "Ainda não existe tabela de escalões de IRT." })
            : Results.Ok(tabela);
    }

    private static async Task<IResult> IntroduceIncomeTaxScheduleVersionAsync(
        IntroduceIncomeTaxScheduleVersionRequest request,
        IntroduceIncomeTaxScheduleVersion introduceVersion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await introduceVersion.ExecuteAsync(
            request.Brackets,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.LegalInstrument,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            IntroduceScheduleVersionOutcome.Introduced =>
                Results.Created("/fiscal/income-tax-schedule", new { versionId = result.VersionId }),

            // 409 e não 400: a sobreposição não é um campo mal preenchido, é
            // conflito com o que já lá está.
            IntroduceScheduleVersionOutcome.Overlaps =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            // 400: instrumento legal em branco, escalão fora de forma, taxa
            // fora de 0–100, vigência invertida.
            IntroduceScheduleVersionOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["escaloes"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao introduzir a versão de escalões."),
        };
    }

    private static async Task<IResult> DetermineIncomeTaxAsync(
        IIncomeTaxDetermination determination,
        decimal taxableIncome,
        DateOnly taxPointDate,
        CancellationToken cancellationToken)
    {
        var result = await determination.DetermineAsync(
            new IncomeTaxDeterminationRequest(taxableIncome, taxPointDate),
            cancellationToken);

        return result.Outcome switch
        {
            IncomeTaxDeterminationOutcome.Determined => Results.Ok(result.Determination),

            // 404: não há tabela de escalões em vigor para esta data. Recusar
            // é a resposta certa — recair na versão mais próxima inventaria
            // o valor (mesma regra de `TaxDeterminationOutcome.NoRateInForce`).
            IncomeTaxDeterminationOutcome.NoScheduleInForce =>
                Results.NotFound(new { erro = "Não há tabela de escalões de IRT em vigor à data indicada." }),

            _ => Results.Problem("Resultado inesperado na determinação de IRT."),
        };
    }

    private static async Task<IResult> CloseIncomeTaxScheduleVersionAsync(
        Guid versionId,
        CloseVersionRequest request,
        CloseIncomeTaxScheduleVersion closeVersion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await closeVersion.ExecuteAsync(
            versionId, request.EffectiveTo, BuildAuditContext(http), cancellationToken);

        return CloseVersionResponse(result.Outcome, result.Error);
    }

    private static async Task<IResult> GetSubsidyExemptionScheduleAsync(
        SubsidyKind kind,
        GetSubsidyExemptionSchedule getSchedule,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["Subsídio desconhecido."],
            });
        }

        var serie = await getSchedule.ExecuteAsync(kind, cancellationToken);

        return serie is null
            ? Results.NotFound(new { erro = $"Ainda não existe limiar de isenção para '{kind}'." })
            : Results.Ok(serie);
    }

    private static async Task<IResult> IntroduceSubsidyExemptionVersionAsync(
        IntroduceSubsidyExemptionVersionRequest request,
        IntroduceSubsidyExemptionVersion introduceVersion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["Subsídio desconhecido."],
            });
        }

        var result = await introduceVersion.ExecuteAsync(
            request.Kind,
            request.Amount,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.LegalInstrument,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            IntroduceSubsidyExemptionOutcome.Introduced =>
                Results.Created("/fiscal/subsidy-exemptions", new { versionId = result.VersionId }),

            // 409 e não 400: a sobreposição não é um campo mal preenchido, é
            // conflito com o que já lá está.
            IntroduceSubsidyExemptionOutcome.Overlaps =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            // 400: instrumento legal em branco, montante negativo, vigência
            // invertida.
            IntroduceSubsidyExemptionOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["limiar"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao introduzir o limiar de isenção."),
        };
    }

    private static async Task<IResult> CloseSubsidyExemptionVersionAsync(
        Guid versionId,
        CloseSubsidyExemptionVersionRequest request,
        CloseSubsidyExemptionVersion closeVersion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["Subsídio desconhecido."],
            });
        }

        var result = await closeVersion.ExecuteAsync(
            request.Kind, versionId, request.EffectiveTo, BuildAuditContext(http), cancellationToken);

        return CloseVersionResponse(result.Outcome, result.Error);
    }

    private static IResult CloseVersionResponse(CloseVersionOutcome outcome, string? error) => outcome switch
    {
        CloseVersionOutcome.Closed => Results.NoContent(),

        CloseVersionOutcome.NotFound => Results.NotFound(new { erro = "Versão não encontrada." }),

        // 409: já fechada — o pedido está bem formado, colide com o estado.
        CloseVersionOutcome.Conflict =>
            Results.Problem(error, statusCode: StatusCodes.Status409Conflict),

        // 400: vigência invertida (fecha antes de começar).
        CloseVersionOutcome.Rejected =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["vigencia"] = [error!] }),

        _ => Results.Problem("Resultado inesperado ao fechar a versão."),
    };

    private static async Task<IResult> DetermineSubsidyExemptionAsync(
        ISubsidyExemptionDetermination determination,
        SubsidyKind kind,
        DateOnly taxPointDate,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["Subsídio desconhecido."],
            });
        }

        var result = await determination.DetermineAsync(
            new SubsidyExemptionRequest(kind, taxPointDate), cancellationToken);

        return result.Outcome switch
        {
            SubsidyExemptionOutcome.Determined => Results.Ok(result.Exemption),

            // 404: não há limiar em vigor para esta data. Recusar é a
            // resposta certa — recair no limiar mais próximo inventaria o
            // valor (mesma regra de `TaxDeterminationOutcome.NoRateInForce`).
            SubsidyExemptionOutcome.NoThresholdInForce =>
                Results.NotFound(new { erro = "Não há limiar de isenção em vigor para este subsídio à data indicada." }),

            _ => Results.Problem("Resultado inesperado na determinação do limiar de isenção."),
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

public sealed record IntroduceIncomeTaxScheduleVersionRequest(
    IReadOnlyList<Rivo.Fiscal.Domain.NewIncomeTaxBracket> Brackets,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string LegalInstrument);

public sealed record IntroduceSubsidyExemptionVersionRequest(
    SubsidyKind Kind,
    decimal Amount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string LegalInstrument);

/// <param name="EffectiveTo">A partir de quando deixa de valer. Inclusivo.</param>
public sealed record CloseVersionRequest(DateOnly EffectiveTo);

public sealed record CloseSubsidyExemptionVersionRequest(SubsidyKind Kind, DateOnly EffectiveTo);

/// <summary>
/// A identidade fiscal da empresa, como o cliente a envia.
///
/// <para>
/// Os cinco primeiros campos são obrigatórios porque são os que o
/// <c>Header</c> do SAF-T exige e os que um documento impresso não pode omitir.
/// Os restantes melhoram o documento e não o invalidam se faltarem.
/// </para>
/// </summary>
public sealed record TaxEntityRequest(
    string CompanyName,
    string TaxRegistrationNumber,
    string AddressDetail,
    string City,
    string Country,
    string? BusinessName = null,
    string? PostalCode = null,
    string? Email = null,
    string? Phone = null,
    string? SoftwareValidationNumber = null);
