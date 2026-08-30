using Rivo.Audit.Contracts;
using Rivo.Fiscal.Application.Abstractions;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Application.UseCases;

/// <summary>Lê a tabela de escalões de IRT, com todas as suas versões.</summary>
public sealed class GetIncomeTaxSchedule(IIncomeTaxScheduleStore store)
{
    public async Task<IncomeTaxScheduleView?> ExecuteAsync(CancellationToken cancellationToken)
    {
        var tabela = await store.FindAsync(cancellationToken);
        return tabela is null ? null : ToView(tabela);
    }

    internal static IncomeTaxScheduleView ToView(IncomeTaxSchedule tabela) => new(
        tabela.Id,
        [.. tabela.Versions
            .OrderBy(v => v.EffectiveFrom)
            .Select(v => new IncomeTaxScheduleVersionView(
                v.Id,
                v.EffectiveFrom,
                v.EffectiveTo,
                v.LegalInstrument,
                [.. v.Brackets
                    .OrderBy(b => b.LowerBound)
                    .Select(b => new IncomeTaxBracketView(b.LowerBound, b.FixedPortion, b.Rate))]))]);
}

public sealed record IncomeTaxScheduleView(Guid ScheduleId, IReadOnlyList<IncomeTaxScheduleVersionView> Versions);

public sealed record IncomeTaxScheduleVersionView(
    Guid VersionId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string LegalInstrument,
    IReadOnlyList<IncomeTaxBracketView> Brackets);

public sealed record IncomeTaxBracketView(decimal LowerBound, decimal FixedPortion, decimal Rate);

/// <summary>
/// Acrescenta uma versão à tabela de escalões de IRT — cria a série na
/// primeira vez, mesmo padrão de `OpenTaxRateSchedule` + `IntroduceTaxRate`
/// condensados num só passo, porque só há uma tabela e não faz sentido pedir
/// ao chamador que abra a série antes de a poder usar.
///
/// <para>
/// <strong>Auditada por ADR-011 §5</strong> — mesma razão de `IntroduceTaxRate`:
/// altera o IRT de todos os recibos calculados a partir da data escolhida.
/// </para>
/// </summary>
public sealed class IntroduceIncomeTaxScheduleVersion(IIncomeTaxScheduleStore store, IAuditTrail audit)
{
    public async Task<IntroduceScheduleVersionResult> ExecuteAsync(
        IReadOnlyList<NewIncomeTaxBracket> brackets,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var tabela = await store.FindForUpdateAsync(cancellationToken);
        var novaTabela = tabela is null;

        tabela ??= IncomeTaxSchedule.Open();

        IncomeTaxScheduleVersion versao;

        try
        {
            versao = tabela.Introduce(brackets, effectiveFrom, effectiveTo, legalInstrument);
        }
        catch (InvalidOperationException error)
        {
            // Sobreposição de vigências: pedido bem formado, colide com o que
            // já lá está — conflito, não erro de campo.
            return IntroduceScheduleVersionResult.Overlaps(error.Message);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return IntroduceScheduleVersionResult.Rejected(error.Message);
        }

        if (novaTabela)
        {
            await store.AddAsync(tabela, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FiscalAuditActions.IncomeTaxScheduleVersionIntroduced,
                FiscalAuditEntityTypes.IncomeTaxSchedule,
                tabela.Id.ToString(),
                context,
                NewValue: $$"""{"brackets":{{brackets.Count}},"from":"{{effectiveFrom:yyyy-MM-dd}}","legalInstrument":"{{legalInstrument}}"}"""),
            cancellationToken);

        return IntroduceScheduleVersionResult.Success(versao.Id);
    }
}

public sealed record IntroduceScheduleVersionResult(
    IntroduceScheduleVersionOutcome Outcome, Guid? VersionId, string? Error)
{
    public static IntroduceScheduleVersionResult Success(Guid versionId) =>
        new(IntroduceScheduleVersionOutcome.Introduced, versionId, null);

    public static IntroduceScheduleVersionResult Rejected(string error) =>
        new(IntroduceScheduleVersionOutcome.Rejected, null, error);

    public static IntroduceScheduleVersionResult Overlaps(string error) =>
        new(IntroduceScheduleVersionOutcome.Overlaps, null, error);
}

public enum IntroduceScheduleVersionOutcome
{
    Introduced,

    /// <summary>Campo mal preenchido. O chamador corrige o pedido — 400.</summary>
    Rejected,

    /// <summary>Colide com uma vigência existente — 409.</summary>
    Overlaps,
}
