namespace Rivo.Hr.Domain;

/// <summary>
/// Benefício do catálogo da empresa — seguro de saúde, subsídio de transporte,
/// telemóvel.
///
/// <para>
/// <strong>Catálogo e adesão são coisas separadas.</strong> O benefício existe
/// independentemente de alguém o ter: é aqui que se define o que a empresa
/// oferece, e em <see cref="BenefitEnrolment"/> quem o tem e desde quando.
/// Juntá-los obrigaria a repetir a definição por cada colaborador.
/// </para>
///
/// <para>
/// <strong>O valor é referência, não é remuneração processada.</strong>
/// Traduzi-lo em folha — sujeito a IRT, isento, em espécie — é de `payroll`.
/// </para>
/// </summary>
public sealed class Benefit
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private Benefit()
    {
        Name = string.Empty;
        Kind = string.Empty;
        Currency = string.Empty;
    }

    private Benefit(Guid id, string name, string kind, decimal monthlyValue, string currency, string? description)
    {
        Id = id;
        Name = name;
        Kind = kind;
        MonthlyValue = monthlyValue;
        Currency = currency;
        Description = description;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// Natureza do benefício — "saude", "transporte", "alimentacao".
    ///
    /// <para>
    /// Texto livre e não enumeração: o catálogo de tipos é decisão de negócio
    /// que muda com a política da empresa, e fixá-lo em código obrigaria a uma
    /// migração para acrescentar um tipo novo.
    /// </para>
    /// </summary>
    public string Kind { get; private set; }

    /// <summary>Valor mensal de referência. Zero é válido — nem todo o benefício tem valor monetário.</summary>
    public decimal MonthlyValue { get; private set; }

    public string Currency { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Um benefício descontinuado deixa de aceitar adesões novas, mas
    /// <strong>não cancela as existentes</strong>: quem já o tem mantém-no até
    /// alguém decidir o contrário. Por isso desactiva-se em vez de se apagar.
    /// </summary>
    public bool IsActive { get; private set; }

    public static Benefit Create(
        string name,
        string kind,
        decimal monthlyValue,
        string currency,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyValue);

        return new Benefit(
            Guid.CreateVersion7(),
            name.Trim(),
            kind.Trim().ToLowerInvariant(),
            monthlyValue,
            NormaliseCurrency(currency),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    internal static string NormaliseCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var normalised = currency.Trim().ToUpperInvariant();

        if (normalised.Length != 3)
        {
            throw new ArgumentException(
                "A moeda é um código ISO 4217 de três letras, por exemplo AOA.",
                nameof(currency));
        }

        return normalised;
    }
}

/// <summary>
/// Adesão de um Colaborador a um Benefício.
/// </summary>
public sealed class BenefitEnrolment
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private BenefitEnrolment() { }

    private BenefitEnrolment(Guid id, Guid employeeId, Guid benefitId, DateOnly startsOn)
    {
        Id = id;
        EmployeeId = employeeId;
        BenefitId = benefitId;
        StartsOn = startsOn;
        Status = BenefitEnrolmentStatus.Active;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid BenefitId { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly? CancelledOn { get; private set; }

    public BenefitEnrolmentStatus Status { get; private set; }

    /// <summary>
    /// Adere um colaborador a um benefício.
    ///
    /// <para>
    /// Recebe o <see cref="Benefit"/> e não só o identificador porque a regra
    /// — não se adere ao que está descontinuado — precisa de o consultar. É a
    /// invariante a viver no domínio em vez de no caso de uso.
    /// </para>
    /// </summary>
    public static BenefitEnrolment Enrol(Guid employeeId, Benefit benefit, DateOnly startsOn)
    {
        ArgumentNullException.ThrowIfNull(benefit);

        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A adesão pertence sempre a um colaborador.", nameof(employeeId));
        }

        if (!benefit.IsActive)
        {
            throw new InvalidOperationException(
                "Não se pode aderir a um benefício descontinuado.");
        }

        return new BenefitEnrolment(Guid.CreateVersion7(), employeeId, benefit.Id, startsOn);
    }

    /// <summary>
    /// Esteve activa nesta data?
    ///
    /// <para>
    /// <strong>Pergunta temporal, e por isso não consulta o
    /// <see cref="Status"/>.</strong> O cancelamento preenche
    /// <see cref="CancelledOn"/>, que já delimita o período — consultar
    /// também o estado faria uma adesão cancelada responder "nunca esteve
    /// activa", inclusive para datas em que esteve. O parâmetro de data
    /// passaria a não querer dizer nada.
    /// </para>
    /// </summary>
    public bool IsActiveOn(DateOnly date) =>
        date >= StartsOn && (CancelledOn is null || date < CancelledOn);

    public void Cancel(DateOnly on)
    {
        if (Status != BenefitEnrolmentStatus.Active)
        {
            throw new InvalidOperationException("Esta adesão já foi cancelada.");
        }

        if (on < StartsOn)
        {
            throw new ArgumentException(
                "O cancelamento não pode ser anterior ao início da adesão.",
                nameof(on));
        }

        CancelledOn = on;
        Status = BenefitEnrolmentStatus.Cancelled;
    }
}

public enum BenefitEnrolmentStatus
{
    Active,
    Cancelled,
}
