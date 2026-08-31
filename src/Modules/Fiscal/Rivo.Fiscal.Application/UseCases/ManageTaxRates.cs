using Rivo.Audit.Contracts;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Contracts;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.UseCases;

/// <summary>Lista as séries de taxa e as suas versões.</summary>
public sealed class ListTaxRates(ITaxRateStore store)
{
    public async Task<IReadOnlyList<TaxRateScheduleView>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var series = await store.ListAsync(cancellationToken);

        return [.. series.Select(ToView)];
    }

    internal static TaxRateScheduleView ToView(TaxRateSchedule schedule) =>
        new(
            schedule.Id,
            ToContract(schedule.Kind),
            schedule.Code,
            schedule.Description,
            schedule.RequiresExemptionCode,
            [.. schedule.Versions
                .OrderBy(version => version.EffectiveFrom)
                .Select(version => new TaxRateVersionView(
                    version.Id,
                    version.Percentage,
                    version.EffectiveFrom,
                    version.EffectiveTo,
                    version.LegalInstrument))]);

    /// <summary>
    /// Traduz o vocabulário do domínio para o publicado.
    ///
    /// <para>
    /// Os dois enumerados são iguais hoje e existem em duplicado de propósito —
    /// o domínio não referencia os contratos (ADR-010, e é o mesmo padrão de
    /// <c>EmployeeStatus</c> em `hr`). É esta camada que traduz, e o
    /// <c>switch</c> exaustivo faz o compilador avisar quando um dos lados
    /// crescer sem o outro.
    /// </para>
    /// </summary>
    internal static Contracts.TaxKind ToContract(Domain.TaxKind kind) => kind switch
    {
        Domain.TaxKind.ValueAdded => Contracts.TaxKind.ValueAdded,
        Domain.TaxKind.EmployeeSocialSecurity => Contracts.TaxKind.EmployeeSocialSecurity,
        Domain.TaxKind.EmployerSocialSecurity => Contracts.TaxKind.EmployerSocialSecurity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Imposto sem correspondência publicada."),
    };

    internal static Domain.TaxKind ToDomain(Contracts.TaxKind kind) => kind switch
    {
        Contracts.TaxKind.ValueAdded => Domain.TaxKind.ValueAdded,
        Contracts.TaxKind.EmployeeSocialSecurity => Domain.TaxKind.EmployeeSocialSecurity,
        Contracts.TaxKind.EmployerSocialSecurity => Domain.TaxKind.EmployerSocialSecurity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Imposto sem correspondência no domínio."),
    };
}

public sealed record TaxRateScheduleView(
    Guid ScheduleId,
    Contracts.TaxKind Kind,
    string Code,
    string Description,
    bool RequiresExemptionCode,
    IReadOnlyList<TaxRateVersionView> Versions);

public sealed record TaxRateVersionView(
    Guid VersionId,
    decimal Percentage,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string LegalInstrument);

/// <summary>Abre uma série de taxa para um imposto e código.</summary>
public sealed class OpenTaxRateSchedule(ITaxRateStore store, IAuditTrail audit)
{
    public async Task<OpenScheduleResult> ExecuteAsync(
        Contracts.TaxKind kind,
        string code,
        string description,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var dominio = ManageTaxRatesMapping.ToDomain(kind);
        var normalizado = (code ?? string.Empty).Trim().ToUpperInvariant();

        // Duas séries para o mesmo imposto e código tornariam a determinação
        // ambígua da mesma maneira que duas versões sobrepostas — só que uma
        // camada acima, onde o agregado não consegue ver.
        if (await store.FindAsync(dominio, normalizado, cancellationToken) is not null)
        {
            return OpenScheduleResult.Failure($"Já existe uma série para o código '{normalizado}'.");
        }

        TaxRateSchedule serie;

        try
        {
            serie = TaxRateSchedule.Open(dominio, normalizado, description);
        }
        catch (ArgumentException error)
        {
            return OpenScheduleResult.Failure(error.Message);
        }

        await store.AddAsync(serie, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FiscalAuditActions.ScheduleOpened,
                FiscalAuditEntityTypes.TaxRateSchedule,
                serie.Id.ToString(),
                context,
                NewValue: $$"""{"kind":"{{kind}}","code":"{{serie.Code}}"}"""),
            cancellationToken);

        return OpenScheduleResult.Success(serie.Id);
    }
}

public sealed record OpenScheduleResult(bool Succeeded, Guid? ScheduleId, string? Error)
{
    public static OpenScheduleResult Success(Guid scheduleId) => new(true, scheduleId, null);

    public static OpenScheduleResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// Acrescenta uma versão de taxa a uma série.
///
/// <para>
/// <strong>Auditada por ADR-011 §5.</strong> Introduzir uma versão de regra
/// fiscal é operação de dados, não um deploy — e altera o valor de todas as
/// facturas emitidas a partir da data escolhida. Sem rasto, "porquê este valor"
/// fica sem resposta.
/// </para>
/// </summary>
public sealed class IntroduceTaxRate(ITaxRateStore store, IAuditTrail audit)
{
    public async Task<IntroduceRateResult> ExecuteAsync(
        Guid scheduleId,
        decimal percentage,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var serie = await store.FindByIdAsync(scheduleId, cancellationToken);

        if (serie is null)
        {
            return IntroduceRateResult.NotFound();
        }

        Guid versionId;

        try
        {
            versionId = serie.Introduce(percentage, effectiveFrom, effectiveTo, legalInstrument).Id;
        }
        catch (InvalidOperationException error)
        {
            // Sobreposição de vigências: o pedido está bem formado e colide com
            // o que já lá está. Corrige-se fechando a versão anterior, não
            // reescrevendo o pedido — logo é conflito, não erro de campo.
            return IntroduceRateResult.Overlaps(error.Message);
        }
        catch (ArgumentException error)
        {
            // Instrumento legal em branco, taxa fora de 0–100, vigência que
            // termina antes de começar: campos mal preenchidos, e o chamador
            // corrige-os no próprio pedido.
            return IntroduceRateResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FiscalAuditActions.RateIntroduced,
                FiscalAuditEntityTypes.TaxRateSchedule,
                serie.Id.ToString(),
                context,
                NewValue: $$"""
                    {"code":"{{serie.Code}}","percentage":{{percentage}},"from":"{{effectiveFrom:yyyy-MM-dd}}","legalInstrument":"{{legalInstrument}}"}
                    """),
            cancellationToken);

        return IntroduceRateResult.Success(versionId);
    }
}

public sealed record IntroduceRateResult(IntroduceRateOutcome Outcome, Guid? VersionId, string? Error)
{
    public static IntroduceRateResult Success(Guid versionId) =>
        new(IntroduceRateOutcome.Introduced, versionId, null);

    public static IntroduceRateResult NotFound() => new(IntroduceRateOutcome.ScheduleNotFound, null, null);

    public static IntroduceRateResult Rejected(string error) =>
        new(IntroduceRateOutcome.Rejected, null, error);

    public static IntroduceRateResult Overlaps(string error) =>
        new(IntroduceRateOutcome.Overlaps, null, error);
}

public enum IntroduceRateOutcome
{
    Introduced,
    ScheduleNotFound,

    /// <summary>Campo mal preenchido. O chamador corrige o pedido — 400.</summary>
    Rejected,

    /// <summary>
    /// Colide com uma vigência existente. O pedido está bem formado; o que
    /// está errado é o estado — 409.
    /// </summary>
    Overlaps,
}

/// <summary>Traduções entre o vocabulário do domínio e o publicado.</summary>
internal static class ManageTaxRatesMapping
{
    internal static Domain.TaxKind ToDomain(Contracts.TaxKind kind) => ListTaxRates.ToDomain(kind);
}

/// <summary>Acções de `fiscal` na trilha de auditoria.</summary>
public static class FiscalAuditActions
{
    public const string ScheduleOpened = "fiscal.tax_rate.schedule_opened";

    public const string RateIntroduced = "fiscal.tax_rate.introduced";

    public const string IncomeTaxScheduleVersionIntroduced = "fiscal.income_tax_schedule.version_introduced";

    public const string SubsidyExemptionVersionIntroduced = "fiscal.subsidy_exemption.version_introduced";
}

public static class FiscalAuditEntityTypes
{
    public const string TaxRateSchedule = "fiscal.tax_rate_schedule";

    public const string IncomeTaxSchedule = "fiscal.income_tax_schedule";

    public const string SubsidyExemptionSchedule = "fiscal.subsidy_exemption_schedule";
}
