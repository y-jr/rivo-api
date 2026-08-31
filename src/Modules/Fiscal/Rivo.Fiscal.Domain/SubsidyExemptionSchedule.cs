namespace Rivo.Fiscal.Domain;

/// <summary>
/// Os subsídios com tratamento próprio no IRT. Só os que têm limiar de
/// isenção — Férias e Natal ficam de fora porque são tributados normalmente,
/// sem excepção nenhuma (confirmado pelo utilizador, ver
/// `modules/payroll.md`).
/// </summary>
public enum SubsidyKind
{
    FoodAllowance,
    TransportAllowance,
}

/// <summary>
/// A série de versões do limiar de isenção de um subsídio ao longo do tempo.
///
/// <para>
/// Mesmo padrão de <see cref="TaxRateSchedule"/> — a raiz é a série e não a
/// versão, porque a invariante que interessa (as versões não se sobrepõem) é
/// sobre o conjunto. A diferença é o que cada versão guarda: aqui é um
/// montante (o "isento até"), não uma percentagem.
/// </para>
///
/// <para>
/// <strong>Um limiar de isenção é regra fiscal tanto quanto uma taxa ou um
/// escalão</strong> (ADR-011) — não é menos dado só por ser um valor fixo em
/// Kwanzas em vez de uma percentagem. Fica versionado com vigência, nunca
/// como constante em código.
/// </para>
/// </summary>
public sealed class SubsidyExemptionSchedule
{
    private readonly List<SubsidyExemptionVersion> _versions = [];

    private SubsidyExemptionSchedule(Guid id, SubsidyKind kind)
    {
        Id = id;
        Kind = kind;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private SubsidyExemptionSchedule()
    {
    }

    public Guid Id { get; private set; }

    public SubsidyKind Kind { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<SubsidyExemptionVersion> Versions => _versions;

    public static SubsidyExemptionSchedule Open(SubsidyKind kind) => new(Guid.CreateVersion7(), kind);

    /// <summary>
    /// Acrescenta uma versão do limiar, com o diploma que a fixou.
    ///
    /// <para>
    /// <strong>Não substitui a anterior</strong> — mesma razão de
    /// <see cref="TaxRateSchedule.Introduce"/>: folhas já calculadas
    /// continuam a depender do limiar que estava em vigor à data delas.
    /// </para>
    /// </summary>
    /// <param name="effectiveTo">Nulo para a versão corrente, sem fim previsto.</param>
    public SubsidyExemptionVersion Introduce(
        decimal amount,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "O limiar de isenção não pode ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(legalInstrument))
        {
            throw new ArgumentException(
                "Uma versão de limiar regista sempre o instrumento legal que a fixou (ADR-011).",
                nameof(legalInstrument));
        }

        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "A vigência não pode terminar antes de começar.", nameof(effectiveTo));
        }

        var candidata = new SubsidyExemptionVersion(
            Guid.CreateVersion7(), amount, effectiveFrom, effectiveTo, legalInstrument.Trim());

        var sobreposta = _versions.FirstOrDefault(existente => existente.OverlapsWith(candidata));

        if (sobreposta is not null)
        {
            throw new InvalidOperationException(
                $"A vigência sobrepõe-se à versão que vigora desde {sobreposta.EffectiveFrom:yyyy-MM-dd}. " +
                "Dois limiares em vigor à mesma data tornam a determinação ambígua — feche o anterior primeiro.");
        }

        _versions.Add(candidata);

        return candidata;
    }

    /// <summary>
    /// A versão em vigor à data pedida, ou <c>null</c> se não houver nenhuma.
    /// Devolver nulo é a resposta certa — mesma razão de
    /// <see cref="TaxRateSchedule.InForceOn"/>.
    /// </summary>
    public SubsidyExemptionVersion? InForceOn(DateOnly date) =>
        _versions.SingleOrDefault(version => version.IsInForceOn(date));
}

/// <summary>
/// Uma versão do limiar de isenção e o período em que vigorou.
///
/// <para>
/// Imutável depois de introduzida: é facto histórico, e alterá-la mudaria
/// retroactivamente o IRT de folhas já calculadas. Corrigir é fechar esta e
/// introduzir outra.
/// </para>
/// </summary>
public sealed class SubsidyExemptionVersion
{
    internal SubsidyExemptionVersion(
        Guid id,
        decimal amount,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument)
    {
        Id = id;
        Amount = amount;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        LegalInstrument = legalInstrument;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private SubsidyExemptionVersion() => LegalInstrument = string.Empty;

    public Guid Id { get; private set; }

    /// <summary>O "isento até" — em Kwanzas, por mês.</summary>
    public decimal Amount { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Nulo na versão corrente. Inclusivo quando preenchido.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public string LegalInstrument { get; private set; }

    public bool IsInForceOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    public bool OverlapsWith(SubsidyExemptionVersion other)
    {
        var comecaAntesDoFimDoOutro = other.EffectiveTo is null || EffectiveFrom <= other.EffectiveTo;
        var acabaDepoisDoInicioDoOutro = EffectiveTo is null || EffectiveTo >= other.EffectiveFrom;

        return comecaAntesDoFimDoOutro && acabaDepoisDoInicioDoOutro;
    }
}
