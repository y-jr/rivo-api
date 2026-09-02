namespace Rivo.Finance.Domain;

/// <summary>
/// Versão do plano de contas, com origem legal e vigência explícita.
///
/// <para>
/// O detalhe importante é que um plano de contas não é um conjunto de contas
/// fixas: a contabilista/cliente pode ter versões diferentes ao longo do tempo,
/// com base em alterações legais ou na configuração da entidade. O motor deve
/// preservar a história e nunca alterar retroactivamente um lançamento antigo.
/// </para>
/// </summary>
public sealed class ChartOfAccountsVersion
{
    private readonly List<LedgerAccount> _accounts = [];

    private ChartOfAccountsVersion(
        string jurisdiction,
        string name,
        string version,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        Id = Guid.CreateVersion7();
        Jurisdiction = jurisdiction;
        Name = name;
        Version = version;
        Source = source;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = true;
    }

    private ChartOfAccountsVersion()
    {
        Jurisdiction = string.Empty;
        Name = string.Empty;
        Version = string.Empty;
        Source = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Jurisdiction { get; private set; }

    public string Name { get; private set; }

    public string Version { get; private set; }

    public string Source { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<LedgerAccount> Accounts => _accounts;

    public static ChartOfAccountsVersion Create(
        string jurisdiction,
        string name,
        string version,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction))
        {
            throw new ArgumentException("A versão do plano exige jurisdição.", nameof(jurisdiction));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A versão do plano exige nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A versão do plano exige identificador de versão.", nameof(version));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("A versão do plano exige origem legal ou documental.", nameof(source));
        }

        if (effectiveTo is { } fim && fim < effectiveFrom)
        {
            throw new ArgumentException("A vigência final não pode ser anterior à vigência inicial.", nameof(effectiveTo));
        }

        return new ChartOfAccountsVersion(
            jurisdiction.Trim(),
            name.Trim(),
            version.Trim(),
            source.Trim(),
            effectiveFrom,
            effectiveTo);
    }

    public static ChartOfAccountsVersion BootstrapDevelopment(
        string jurisdiction = "ANGOLA",
        string name = "PGC",
        string version = "BOOTSTRAP-DEV",
        string source = "Bootstrap de desenvolvimento — não substitui o PGC oficial",
        DateOnly? effectiveTo = null)
    {
        var versao = Create(
            jurisdiction,
            name,
            version,
            source,
            DateOnly.FromDateTime(DateTime.UtcNow),
            effectiveTo);

        versao.AddAccounts(BootstrapChartOfAccounts.Load());
        return versao;
    }

    public void AddAccounts(IEnumerable<LedgerAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        foreach (var account in accounts)
        {
            account.AssignToVersion(Id);
            _accounts.Add(account);
        }
    }

    public void Deactivate() => IsActive = false;
}
