namespace Rivo.Fiscal.Domain;

/// <summary>
/// A série de versões da tabela de escalões de IRT ao longo do tempo.
///
/// <para>
/// <strong>Singleton de facto</strong> — ao contrário de <see cref="TaxRateSchedule"/>,
/// que existe uma por imposto e código, só há uma tabela de escalões de IRT
/// no sistema. Não tem <c>Kind</c> nem <c>Code</c> por isso: não há nada que
/// a distinga de outra, porque não há outra.
/// </para>
///
/// <para>
/// A raiz é a série, não a versão — mesma razão de <see cref="TaxRateSchedule"/>:
/// a invariante que interessa (as versões não se sobrepõem) é sobre o
/// conjunto.
/// </para>
/// </summary>
public sealed class IncomeTaxSchedule
{
    private readonly List<IncomeTaxScheduleVersion> _versions = [];

    private IncomeTaxSchedule(Guid id)
    {
        Id = id;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private IncomeTaxSchedule()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<IncomeTaxScheduleVersion> Versions => _versions;

    public static IncomeTaxSchedule Open() => new(Guid.CreateVersion7());

    /// <summary>
    /// Acrescenta uma versão da tabela, com o diploma que a fixou.
    ///
    /// <para>
    /// <strong>Não substitui a anterior</strong> — mesma razão de
    /// <see cref="TaxRateSchedule.Introduce"/>: recibos já emitidos continuam
    /// a depender da tabela que estava em vigor à data deles.
    /// </para>
    /// </summary>
    /// <param name="effectiveTo">Nulo para a versão corrente, sem fim previsto.</param>
    public IncomeTaxScheduleVersion Introduce(
        IReadOnlyList<NewIncomeTaxBracket> brackets,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument)
    {
        if (string.IsNullOrWhiteSpace(legalInstrument))
        {
            throw new ArgumentException(
                "Uma versão de escalões regista sempre o instrumento legal que a fixou (ADR-011).",
                nameof(legalInstrument));
        }

        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "A vigência não pode terminar antes de começar.", nameof(effectiveTo));
        }

        if (brackets is null or { Count: 0 })
        {
            throw new ArgumentException(
                "Uma tabela de escalões precisa de pelo menos um escalão.", nameof(brackets));
        }

        var ordenados = brackets.OrderBy(b => b.LowerBound).ToList();

        for (var i = 0; i < ordenados.Count; i++)
        {
            var escalao = ordenados[i];

            if (escalao.Rate is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(brackets), escalao.Rate, "A taxa de um escalão está entre 0 e 100 por cento.");
            }

            if (escalao.FixedPortion < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(brackets), escalao.FixedPortion, "A parcela fixa não pode ser negativa.");
            }

            if (i > 0 && ordenados[i].LowerBound == ordenados[i - 1].LowerBound)
            {
                throw new ArgumentException(
                    $"Dois escalões com o mesmo limiar ({escalao.LowerBound}) tornam a selecção ambígua.",
                    nameof(brackets));
            }
        }

        if (ordenados[0].LowerBound != 0)
        {
            throw new ArgumentException(
                "O primeiro escalão tem de começar em zero — é a isenção, sem ela não há para onde " +
                "cair um rendimento abaixo do primeiro limiar.",
                nameof(brackets));
        }

        var candidata = new IncomeTaxScheduleVersion(
            Guid.CreateVersion7(),
            effectiveFrom,
            effectiveTo,
            legalInstrument.Trim(),
            ordenados.Select(b => new IncomeTaxBracket(Guid.CreateVersion7(), b.LowerBound, b.FixedPortion, b.Rate)));

        var sobreposta = _versions.FirstOrDefault(existente => existente.OverlapsWith(candidata));

        if (sobreposta is not null)
        {
            throw new InvalidOperationException(
                $"A vigência sobrepõe-se à versão que vigora desde {sobreposta.EffectiveFrom:yyyy-MM-dd}. " +
                "Duas tabelas em vigor à mesma data tornam a determinação ambígua — feche a anterior primeiro.");
        }

        _versions.Add(candidata);

        return candidata;
    }

    /// <summary>
    /// A versão em vigor à data pedida, ou <c>null</c> se não houver nenhuma.
    /// Devolver nulo é a resposta certa — ver <see cref="TaxRateSchedule.InForceOn"/>.
    /// </summary>
    public IncomeTaxScheduleVersion? InForceOn(DateOnly date) =>
        _versions.SingleOrDefault(version => version.IsInForceOn(date));
}

/// <param name="LowerBound">O "excesso de" do escalão — 0 para o primeiro.</param>
/// <param name="FixedPortion">A parcela fixa. 0 no escalão de isenção.</param>
/// <param name="Rate">A taxa marginal, em percentagem. 0 no escalão de isenção.</param>
public sealed record NewIncomeTaxBracket(decimal LowerBound, decimal FixedPortion, decimal Rate);

/// <summary>
/// Uma versão da tabela de escalões e o período em que vigorou.
///
/// <para>
/// Imutável depois de introduzida — mesma razão de <see cref="TaxRateVersion"/>:
/// é facto histórico, e alterá-la mudaria retroactivamente o imposto de
/// recibos já emitidos.
/// </para>
/// </summary>
public sealed class IncomeTaxScheduleVersion
{
    private readonly List<IncomeTaxBracket> _brackets = [];

    internal IncomeTaxScheduleVersion(
        Guid id,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument,
        IEnumerable<IncomeTaxBracket> brackets)
    {
        Id = id;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        LegalInstrument = legalInstrument;
        _brackets.AddRange(brackets);
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private IncomeTaxScheduleVersion() => LegalInstrument = string.Empty;

    public Guid Id { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Nulo na versão corrente. Inclusivo quando preenchido.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public string LegalInstrument { get; private set; }

    public IReadOnlyList<IncomeTaxBracket> Brackets => _brackets;

    public bool IsInForceOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    public bool OverlapsWith(IncomeTaxScheduleVersion other)
    {
        var comecaAntesDoFimDoOutro = other.EffectiveTo is null || EffectiveFrom <= other.EffectiveTo;
        var acabaDepoisDoInicioDoOutro = EffectiveTo is null || EffectiveTo >= other.EffectiveFrom;

        return comecaAntesDoFimDoOutro && acabaDepoisDoInicioDoOutro;
    }

    /// <summary>
    /// Calcula o IRT devido sobre a matéria colectável.
    ///
    /// <para>
    /// <c>IRT = Parcela Fixa + (Matéria Colectável − Excesso de) × Taxa</c>,
    /// aplicada a partir do 2.º escalão (`modules/payroll.md`). O escalão de
    /// isenção (parcela 0, taxa 0) satisfaz a mesma fórmula sem precisar de
    /// caso especial: qualquer matéria colectável nesse escalão dá zero.
    /// </para>
    ///
    /// <para>
    /// <strong>Selecção do escalão:</strong> o de maior "excesso de" que a
    /// matéria colectável ainda ultrapassa — nunca iguala. É o que faz
    /// 150.000 cair no escalão de isenção e 150.001 já não: o limiar em si
    /// pertence ao escalão anterior.
    /// </para>
    /// </summary>
    public IncomeTaxBracket SelectBracket(decimal taxableIncome)
    {
        if (taxableIncome < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(taxableIncome), taxableIncome, "A matéria colectável não pode ser negativa.");
        }

        return _brackets.Where(b => taxableIncome > b.LowerBound).MaxBy(b => b.LowerBound)
            ?? _brackets.MinBy(b => b.LowerBound)!;
    }

    public decimal Compute(decimal taxableIncome)
    {
        var escalao = SelectBracket(taxableIncome);

        return escalao.FixedPortion + (taxableIncome - escalao.LowerBound) * (escalao.Rate / 100m);
    }
}

/// <summary>Um escalão da tabela de IRT — ver a fórmula em <see cref="IncomeTaxScheduleVersion.Compute"/>.</summary>
public sealed class IncomeTaxBracket
{
    internal IncomeTaxBracket(Guid id, decimal lowerBound, decimal fixedPortion, decimal rate)
    {
        Id = id;
        LowerBound = lowerBound;
        FixedPortion = fixedPortion;
        Rate = rate;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private IncomeTaxBracket()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>O "excesso de". Zero no escalão de isenção.</summary>
    public decimal LowerBound { get; private set; }

    /// <summary>A parcela fixa. Zero no escalão de isenção.</summary>
    public decimal FixedPortion { get; private set; }

    /// <summary>A taxa marginal, em percentagem (16 para 16%). Zero no escalão de isenção.</summary>
    public decimal Rate { get; private set; }
}
