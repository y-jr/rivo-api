using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Xml;
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

        /*
         * Correccao do codigo do SAF-T de uma serie.
         *
         * Existe para desfazer um beco: uma serie de IVA aberta com um codigo
         * que a AGT nao aceita bloqueia a exportacao para sempre -- nada se
         * elimina (BR-14) e o codigo nao se alterava. Desde 2026-09-08 a
         * abertura ja o recusa, mas as series anteriores continuam la.
         */
        group.MapPatch("/tax-rates/{scheduleId:guid}/code", CorrectCodeAsync)
            .RequireAuthorization(FiscalPermissions.RatesWrite);

        // Determinação: o que `commercial` e `finance` fazem por contrato, aqui
        // exposto para se poder conferir o que a emissão vai receber.
        group.MapGet("/tax-rates/determination", DetermineAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        /*
         * A exportacao SAF-T.
         *
         * ⚠ Permissao propria (`fiscal.saft.export`) e nao `RatesRead`: quem
         * exporta leva a contabilidade, os clientes, os fornecedores e os
         * documentos comerciais da empresa inteira num ficheiro.
         *
         * ⚠ O ficheiro **nao tem validade legal** -- sai com
         * `SoftwareValidationNumber` a "0", porque o Rivo nao esta certificado
         * pela AGT (ADR-036). Valido na forma nao e aceite.
         */
        group.MapGet("/saft", ExportSaftAsync)
            .RequireAuthorization(FiscalPermissions.SaftExport);

        group.MapGet("/income-tax-schedule", GetIncomeTaxScheduleAsync)
            .RequireAuthorization(FiscalPermissions.RatesRead);

        // Escrita de escalões é configuração sensível: altera o IRT de todos
        // os recibos calculados a partir da data escolhida (ADR-011 §5).
        group.MapPost("/income-tax-schedule/versions", IntroduceIncomeTaxScheduleVersionAsync)
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

        group.MapGet("/subsidy-exemptions/determination", DetermineSubsidyExemptionAsync)
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
            IntroduceRateOutcome.Overlaps =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            // 400: instrumento legal em branco, taxa fora de 0–100, vigência
            // invertida. Aqui o pedido é que está mal, e corrige-se no pedido.
            IntroduceRateOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["taxa"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao introduzir a taxa."),
        };
    }

    private static async Task<IResult> CorrectCodeAsync(
        Guid scheduleId,
        CorrectCodeRequest request,
        CorrectTaxRateCode correctCode,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await correctCode.ExecuteAsync(
            scheduleId, request.Code, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CorrectCodeOutcome.Corrected => Results.Ok(new { code = result.Code }),

            CorrectCodeOutcome.ScheduleNotFound =>
                Results.NotFound(new { erro = "Série de taxa não encontrada." }),

            // 400: o código não é aceite pelo SAF-T, ou já existe outra série
            // com ele. Nos dois casos corrige-se no pedido.
            CorrectCodeOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["codigo"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao corrigir o código."),
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

    private static async Task<IResult> GetSubsidyExemptionScheduleAsync(
        SubsidyKind kind,
        GetSubsidyExemptionSchedule getSchedule,
        CancellationToken cancellationToken)
    {
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

    private static async Task<IResult> DetermineSubsidyExemptionAsync(
        ISubsidyExemptionDetermination determination,
        SubsidyKind kind,
        DateOnly taxPointDate,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Devolve o ficheiro SAF-T do período como descarga.
    ///
    /// <para>
    /// <strong>Descarga e não JSON.</strong> O SAF-T é XML por definição do
    /// esquema, e quem o pede quer um ficheiro para entregar — embrulhá-lo
    /// numa resposta JSON obrigaria o cliente a desembrulhá-lo para gravar o
    /// mesmo XML.
    /// </para>
    ///
    /// <para>
    /// A codificação é <c>windows-1252</c>, que é o que a declaração XML do
    /// ficheiro anuncia. UTF-8 seria mais natural em 2026 e faria o documento
    /// mentir sobre si próprio.
    /// </para>
    /// </summary>
    private static async Task<IResult> ExportSaftAsync(
        int fiscalYear,
        DateOnly? from,
        DateOnly? to,
        ExportSaftFile export,
        CancellationToken cancellationToken)
    {
        // Sem janela, o ano civil inteiro — que é o pedido normal da AGT.
        var inicio = from ?? new DateOnly(fiscalYear, 1, 1);
        var fim = to ?? new DateOnly(fiscalYear, 12, 31);

        var resultado = await export.ExecuteAsync(fiscalYear, inicio, fim, cancellationToken);

        if (resultado.Outcome is ExportSaftOutcome.Rejected)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["saft"] = [resultado.Error!] });
        }

        using var memoria = new MemoryStream();
        using (var escritor = XmlWriter.Create(
            memoria,
            new XmlWriterSettings
            {
                Encoding = System.Text.Encoding.Latin1,
                Indent = true,
            }))
        {
            resultado.File!.Save(escritor);
        }

        return Results.File(
            memoria.ToArray(),
            "application/xml",
            $"SAFT-AO-{fiscalYear}-{inicio:yyyyMMdd}-{fim:yyyyMMdd}.xml");
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

public sealed record CorrectCodeRequest(string Code);

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
