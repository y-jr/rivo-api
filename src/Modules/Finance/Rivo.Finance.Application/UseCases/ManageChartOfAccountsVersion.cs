using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Gestão de versões do plano de contas.
///
/// <para>
/// Cada versão é um conjunto imutável de contas, com origem legal, vigência
/// e estado próprio. O motor não inventa planos — recebe-os carregados pelo
/// utilizador, com referência documental explícita.
/// </para>
/// </summary>
public sealed class CreateChartOfAccountsVersion(ILedgerStore store, IAuditTrail audit)
{
    public async Task<CreateChartOfAccountsVersionResult> ExecuteAsync(
        string jurisdiction,
        string name,
        string version,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Unicidade por (jurisdição, nome, versão) — não há duas iguais.
        if (await store.FindChartVersionByKeyAsync(jurisdiction, name, version, cancellationToken) is not null)
        {
            return CreateChartOfAccountsVersionResult.Duplicate(
                $"Versão do plano ({jurisdiction}, {name}, {version}) já existe.");
        }

        ChartOfAccountsVersion versao;

        try
        {
            versao = ChartOfAccountsVersion.Create(
                jurisdiction,
                name,
                version,
                source,
                effectiveFrom,
                effectiveTo);
        }
        catch (ArgumentException error)
        {
            return CreateChartOfAccountsVersionResult.Rejected(error.Message);
        }

        await store.AddChartVersionAsync(versao, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.ChartOfAccountsVersionCreated,
                FinanceAuditEntityTypes.ChartOfAccountsVersion,
                versao.Id.ToString(),
                context,
                NewValue: $$"""{"jurisdiction":"{{versao.Jurisdiction}}","name":"{{versao.Name}}","version":"{{versao.Revision}}","source":"{{versao.Source}}","effectiveFrom":"{{versao.EffectiveFrom:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return CreateChartOfAccountsVersionResult.Success(versao.Id);
    }
}

public sealed record CreateChartOfAccountsVersionResult(
    CreateChartOfAccountsVersionOutcome Outcome,
    Guid? ChartVersionId,
    string? Error)
{
    public static CreateChartOfAccountsVersionResult Success(Guid id) =>
        new(CreateChartOfAccountsVersionOutcome.Created, id, null);

    public static CreateChartOfAccountsVersionResult Duplicate(string error) =>
        new(CreateChartOfAccountsVersionOutcome.Duplicate, null, error);

    public static CreateChartOfAccountsVersionResult Rejected(string error) =>
        new(CreateChartOfAccountsVersionOutcome.Rejected, null, error);
}

public enum CreateChartOfAccountsVersionOutcome
{
    Created,

    /// <summary>Já existe versão com esta (jurisdição, nome, versão) — 409.</summary>
    Duplicate,

    Rejected,
}

public sealed class ListChartOfAccountsVersions(ILedgerStore store)
{
    public async Task<IReadOnlyList<ChartOfAccountsVersionView>> ExecuteAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var versoes = await store.ListChartVersionsAsync(includeInactive, cancellationToken);

        return [.. versoes.Select(v => new ChartOfAccountsVersionView(
            v.Id,
            v.Jurisdiction,
            v.Name,
            v.Revision,
            v.Source,
            v.EffectiveFrom,
            v.EffectiveTo,
            v.IsActive,
            v.Accounts.Count))];
    }
}

public sealed record ChartOfAccountsVersionView(
    Guid ChartVersionId,
    string Jurisdiction,
    string Name,
    string Version,
    string Source,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    int AccountCount);
