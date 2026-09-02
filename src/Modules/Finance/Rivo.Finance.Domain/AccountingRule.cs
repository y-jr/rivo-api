namespace Rivo.Finance.Domain;

/// <summary>
/// Regra contabilística versionada que traduz um acontecimento de negócio num
/// lançamento contábil.
///
/// <para>
/// A regra não inventa contas. Ela apenas identifica a conta do plano, o lado e
/// a parcela do documento (líquido, imposto, total) que a linha representa. A
/// validação da estrutura do plano existe separada, e as alterações futuras
/// mantêm o histórico da regra.
/// </para>
/// </summary>
public sealed class AccountingRule
{
    private readonly List<AccountingRuleLine> _lines = [];

    private AccountingRule(
        string code,
        string name,
        string sourceType,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        SourceType = sourceType;
        Source = source;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = true;
    }

    private AccountingRule()
    {
        Code = string.Empty;
        Name = string.Empty;
        SourceType = string.Empty;
        Source = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string SourceType { get; private set; }

    public string Source { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<AccountingRuleLine> Lines => _lines;

    public static AccountingRule Create(
        string code,
        string name,
        string sourceType,
        string source,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        IReadOnlyList<AccountingRuleLine> lines)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A regra contabilística precisa de código.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A regra contabilística precisa de descrição.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(sourceType))
        {
            throw new ArgumentException("A regra contabilística exige tipo de origem.", nameof(sourceType));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("A regra contabilística exige referência legal ou documental.", nameof(source));
        }

        if (effectiveTo is { } fim && fim < effectiveFrom)
        {
            throw new ArgumentException("A vigência final não pode ser anterior à vigência inicial.", nameof(effectiveTo));
        }

        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("A regra contabilística precisa de linhas.", nameof(lines));
        }

        var regra = new AccountingRule(
            code.Trim(),
            name.Trim(),
            sourceType.Trim(),
            source.Trim(),
            effectiveFrom,
            effectiveTo);

        foreach (var linha in lines)
        {
            regra._lines.Add(linha);
        }

        regra.ValidateBalance();

        return regra;
    }

    private void ValidateBalance()
    {
        var debitoLiquido = 0;
        var debitoImposto = 0;
        var creditoLiquido = 0;
        var creditoImposto = 0;

        foreach (var linha in _lines)
        {
            switch (linha.Side)
            {
                case EntrySide.Debit:
                    switch (linha.Amount)
                    {
                        case PostingAmount.Net:
                            debitoLiquido++;
                            break;
                        case PostingAmount.Tax:
                            debitoImposto++;
                            break;
                        case PostingAmount.Gross:
                            debitoLiquido++;
                            debitoImposto++;
                            break;
                    }
                    break;

                case EntrySide.Credit:
                    switch (linha.Amount)
                    {
                        case PostingAmount.Net:
                            creditoLiquido++;
                            break;
                        case PostingAmount.Tax:
                            creditoImposto++;
                            break;
                        case PostingAmount.Gross:
                            creditoLiquido++;
                            creditoImposto++;
                            break;
                    }
                    break;
            }
        }

        if (debitoLiquido == 0 && debitoImposto == 0)
        {
            throw new ArgumentException("A regra contabilística tem de ter pelo menos uma linha a débito.");
        }

        if (creditoLiquido == 0 && creditoImposto == 0)
        {
            throw new ArgumentException("A regra contabilística tem de ter pelo menos uma linha a crédito.");
        }

        if (debitoLiquido != creditoLiquido || debitoImposto != creditoImposto)
        {
            throw new ArgumentException(
                "A regra contabilística não equilibra: débito e crédito têm de refletir a mesma composição de líquido e imposto.");
        }
    }

    public void Deactivate() => IsActive = false;
}

public sealed record AccountingRuleLine(
    string AccountCode,
    EntrySide Side,
    PostingAmount Amount,
    string Description);
