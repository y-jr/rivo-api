namespace Rivo.Fiscal.Domain;

/// <summary>
/// A série de versões de uma taxa de imposto ao longo do tempo.
///
/// <para>
/// É o agregado que torna o ADR-011 executável. A regra "nenhuma taxa em
/// código" só produz valor se houver onde as pôr **com vigência**, e se a
/// vigência for imposta: sem a garantia de não sobreposição, perguntar "que
/// taxa vigorava em Março" pode ter duas respostas, e a determinação à data do
/// facto gerador deixa de ser determinística.
/// </para>
///
/// <para>
/// A raiz é a série (imposto + código), não a versão. É a fronteira certa
/// porque a invariante que interessa — as versões não se sobrepõem — é sobre o
/// conjunto e não sobre cada uma.
/// </para>
/// </summary>
public sealed class TaxRateSchedule
{
    private readonly List<TaxRateVersion> _versions = [];

    private TaxRateSchedule(Guid id, TaxKind kind, string code, string description)
    {
        Id = id;
        Kind = kind;
        Code = code;
        Description = description;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private TaxRateSchedule()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public TaxKind Kind { get; private set; }

    /// <summary>
    /// Código do SAF-T que identifica a taxa (<c>ISE</c>, <c>NS</c>, e os
    /// restantes que quem introduz os dados fornece — ver
    /// <see cref="TaxCodes"/>).
    /// </summary>
    public string Code { get; private set; }

    public string Description { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025).
    ///
    /// <para>
    /// <strong>O domínio nunca lhe toca.</strong> Quem o incrementa é o
    /// `SaveChangesAsync` do DbContext, para todas as entidades alteradas de
    /// uma vez. Obrigar cada método que altera estado a lembrar-se disto seria
    /// uma regra que se esquece uma vez e falha em silêncio para sempre.
    /// </para>
    /// </summary>
    public int Version { get; private set; }

    public IReadOnlyList<TaxRateVersion> Versions => _versions;

    /// <summary>
    /// Verdadeiro para os códigos que obrigam a <c>TaxExemptionCode</c>. Ver
    /// <see cref="TaxCodes.RequiresExemptionCode"/>.
    /// </summary>
    public bool RequiresExemptionCode => TaxCodes.RequiresExemptionCode(Code);

    public static TaxRateSchedule Open(TaxKind kind, string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Uma série de taxa precisa do código do SAF-T que a identifica.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Uma série de taxa precisa de descrição — é o que a torna reconhecível a quem " +
                "introduz os dados.",
                nameof(description));
        }

        return new TaxRateSchedule(Guid.CreateVersion7(), kind, code.Trim().ToUpperInvariant(), description.Trim());
    }

    /// <summary>
    /// Acrescenta uma versão da taxa, com o diploma que a fixou.
    ///
    /// <para>
    /// <strong>Não substitui a anterior.</strong> Introduzir uma taxa nova é
    /// acrescentar ao histórico, porque documentos já emitidos continuam a
    /// depender da versão que estava em vigor à data deles.
    /// </para>
    /// </summary>
    /// <param name="effectiveTo">Nulo para a versão corrente, sem fim previsto.</param>
    public TaxRateVersion Introduce(
        decimal percentage,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument)
    {
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage), percentage, "Uma taxa está entre 0 e 100 por cento.");
        }

        // ADR-011 §4: sem o diploma não há rastreabilidade, e "porquê este
        // valor" fica sem resposta na auditoria.
        if (string.IsNullOrWhiteSpace(legalInstrument))
        {
            throw new ArgumentException(
                "Uma versão de taxa regista sempre o instrumento legal que a fixou (ADR-011).",
                nameof(legalInstrument));
        }

        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw new ArgumentException(
                "A vigência não pode terminar antes de começar.", nameof(effectiveTo));
        }

        // Uma isenção com percentagem é uma contradição: ou o código isenta, ou
        // há imposto a liquidar. Apanhado aqui em vez de na conferência.
        if (RequiresExemptionCode && percentage != 0)
        {
            throw new ArgumentException(
                $"O código '{Code}' é de isenção ou de não sujeição e não pode ter taxa " +
                $"diferente de zero.",
                nameof(percentage));
        }

        var candidata = new TaxRateVersion(Guid.CreateVersion7(), percentage, effectiveFrom, effectiveTo, legalInstrument.Trim());

        var sobreposta = _versions.FirstOrDefault(existente => existente.OverlapsWith(candidata));

        if (sobreposta is not null)
        {
            throw new InvalidOperationException(
                $"A vigência sobrepõe-se à versão que vigora desde {sobreposta.EffectiveFrom:yyyy-MM-dd}. " +
                "Duas taxas em vigor à mesma data tornam a determinação ambígua — feche a anterior primeiro.");
        }

        _versions.Add(candidata);

        return candidata;
    }

    /// <summary>
    /// A versão em vigor à data pedida, ou <c>null</c> se não houver nenhuma.
    ///
    /// <para>
    /// <strong>Devolver nulo é a resposta certa</strong>, não um caso a
    /// contornar. Um período sem taxa configurada é um buraco nos dados de
    /// referência, e recair na versão mais próxima inventaria o valor.
    /// </para>
    /// </summary>
    public TaxRateVersion? InForceOn(DateOnly date) =>
        _versions.SingleOrDefault(version => version.IsInForceOn(date));
}

/// <summary>
/// Uma versão de taxa e o período em que vigorou.
///
/// <para>
/// Imutável depois de introduzida: é facto histórico, e alterá-la mudaria
/// retroactivamente o imposto de documentos já emitidos. Corrigir é fechar
/// esta e introduzir outra.
/// </para>
/// </summary>
public sealed class TaxRateVersion
{
    internal TaxRateVersion(
        Guid id,
        decimal percentage,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string legalInstrument)
    {
        Id = id;
        Percentage = percentage;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        LegalInstrument = legalInstrument;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private TaxRateVersion() => LegalInstrument = string.Empty;

    public Guid Id { get; private set; }

    public decimal Percentage { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Nulo na versão corrente. Inclusivo quando preenchido.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public string LegalInstrument { get; private set; }

    /// <summary>
    /// Pergunta puramente temporal: esta versão cobria esta data?
    ///
    /// <para>
    /// Não consulta estado nenhum além das duas datas, e é deliberado. Misturar
    /// aqui uma noção de "activa" faria uma versão fechada responder "nunca
    /// vigorou" mesmo para datas que cobriu — e a determinação de um documento
    /// antigo passaria a falhar por o presente ter mudado.
    /// </para>
    /// </summary>
    public bool IsInForceOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>
    /// Duas versões sobrepõem-se se existir alguma data coberta por ambas.
    /// Fim nulo trata-se como infinito.
    /// </summary>
    public bool OverlapsWith(TaxRateVersion other)
    {
        var comecaAntesDoFimDoOutro = other.EffectiveTo is null || EffectiveFrom <= other.EffectiveTo;
        var acabaDepoisDoInicioDoOutro = EffectiveTo is null || EffectiveTo >= other.EffectiveFrom;

        return comecaAntesDoFimDoOutro && acabaDepoisDoInicioDoOutro;
    }
}
