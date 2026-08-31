using Rivo.Audit.Contracts;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Contracts;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.UseCases;

/// <summary>
/// Traduz <see cref="Contracts.SubsidyKind"/> ↔ <see cref="Domain.SubsidyKind"/>.
///
/// <para>
/// Duplicação deliberada entre Domain e Contracts, mesmo padrão de
/// `ManageTaxRatesMapping` — o domínio não referencia os contratos (ADR-017),
/// e é esta camada que traduz.
/// </para>
/// </summary>
internal static class SubsidyKindMapping
{
    internal static Domain.SubsidyKind ToDomain(Contracts.SubsidyKind kind) => kind switch
    {
        Contracts.SubsidyKind.FoodAllowance => Domain.SubsidyKind.FoodAllowance,
        Contracts.SubsidyKind.TransportAllowance => Domain.SubsidyKind.TransportAllowance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Subsídio sem correspondência no domínio."),
    };
}

/// <summary>Lê a série de limiares de isenção de um subsídio, com todas as suas versões.</summary>
public sealed class GetSubsidyExemptionSchedule(ISubsidyExemptionStore store)
{
    public async Task<SubsidyExemptionScheduleView?> ExecuteAsync(
        Contracts.SubsidyKind kind, CancellationToken cancellationToken)
    {
        var serie = await store.FindAsync(SubsidyKindMapping.ToDomain(kind), cancellationToken);
        return serie is null ? null : ToView(serie);
    }

    internal static SubsidyExemptionScheduleView ToView(SubsidyExemptionSchedule serie) => new(
        serie.Id,
        serie.Kind,
        [.. serie.Versions
            .OrderBy(v => v.EffectiveFrom)
            .Select(v => new SubsidyExemptionVersionView(
                v.Id, v.Amount, v.EffectiveFrom, v.EffectiveTo, v.LegalInstrument))]);
}

public sealed record SubsidyExemptionScheduleView(
    Guid ScheduleId, Domain.SubsidyKind Kind, IReadOnlyList<SubsidyExemptionVersionView> Versions);

public sealed record SubsidyExemptionVersionView(
    Guid VersionId, decimal Amount, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string LegalInstrument);

/// <summary>
/// Acrescenta uma versão ao limiar de isenção de um subsídio — cria a série
/// na primeira vez, mesmo padrão de `IntroduceIncomeTaxScheduleVersion`
/// (condensado num só passo, porque só há uma série por
/// <see cref="Domain.SubsidyKind"/> e não faz sentido pedir ao chamador que a
/// abra antes de a poder usar).
///
/// <para>
/// <strong>Auditada por ADR-011 §5</strong> — mesma razão de
/// `IntroduceTaxRate`: altera a matéria colectável de IRT de todas as folhas
/// calculadas a partir da data escolhida.
/// </para>
/// </summary>
public sealed class IntroduceSubsidyExemptionVersion(ISubsidyExemptionStore store, IAuditTrail audit)
{
    public async Task<IntroduceSubsidyExemptionResult> ExecuteAsync(
        Contracts.SubsidyKind kind,
        decimal amount,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var dominio = SubsidyKindMapping.ToDomain(kind);
        var serie = await store.FindForUpdateAsync(dominio, cancellationToken);
        var novaSerie = serie is null;

        serie ??= SubsidyExemptionSchedule.Open(dominio);

        SubsidyExemptionVersion versao;

        try
        {
            versao = serie.Introduce(amount, effectiveFrom, effectiveTo, legalInstrument);
        }
        catch (InvalidOperationException error)
        {
            // Sobreposição de vigências: pedido bem formado, colide com o que
            // já lá está — conflito, não erro de campo.
            return IntroduceSubsidyExemptionResult.Overlaps(error.Message);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return IntroduceSubsidyExemptionResult.Rejected(error.Message);
        }

        if (novaSerie)
        {
            await store.AddAsync(serie, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FiscalAuditActions.SubsidyExemptionVersionIntroduced,
                FiscalAuditEntityTypes.SubsidyExemptionSchedule,
                serie.Id.ToString(),
                context,
                NewValue: $$"""{"kind":"{{kind}}","amount":{{amount}},"from":"{{effectiveFrom:yyyy-MM-dd}}","legalInstrument":"{{legalInstrument}}"}"""),
            cancellationToken);

        return IntroduceSubsidyExemptionResult.Success(versao.Id);
    }
}

public sealed record IntroduceSubsidyExemptionResult(
    IntroduceSubsidyExemptionOutcome Outcome, Guid? VersionId, string? Error)
{
    public static IntroduceSubsidyExemptionResult Success(Guid versionId) =>
        new(IntroduceSubsidyExemptionOutcome.Introduced, versionId, null);

    public static IntroduceSubsidyExemptionResult Rejected(string error) =>
        new(IntroduceSubsidyExemptionOutcome.Rejected, null, error);

    public static IntroduceSubsidyExemptionResult Overlaps(string error) =>
        new(IntroduceSubsidyExemptionOutcome.Overlaps, null, error);
}

public enum IntroduceSubsidyExemptionOutcome
{
    Introduced,

    /// <summary>Campo mal preenchido. O chamador corrige o pedido — 400.</summary>
    Rejected,

    /// <summary>Colide com uma vigência existente — 409.</summary>
    Overlaps,
}
