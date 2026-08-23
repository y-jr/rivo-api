namespace Rivo.Hr.Domain;

/// <summary>
/// Contrato de Trabalho — a relação laboral entre a empresa e um Colaborador.
///
/// <para>
/// <strong>É de `hr`, e o cálculo da folha não é</strong>
/// (<c>.claude/modules/hr.md</c>). Aqui vive o que foi <em>acordado</em>:
/// tipo, vigência e remuneração base. O que se paga num mês concreto — com
/// horas, subsídios, IRT e INSS — é de `payroll`, que lê este contrato como
/// entrada de cálculo.
/// </para>
///
/// <para>
/// <strong>Não guarda o nome do colaborador.</strong> O ADR-010 é explícito:
/// quem referencia um Colaborador guarda apenas o identificador, e nunca copia
/// nome, departamento ou cargo para as suas tabelas. Copiado, o nome fica
/// desactualizado no dia em que alguém o corrigir no sítio certo.
/// </para>
/// </summary>
public sealed class EmploymentContract
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private EmploymentContract() => Currency = string.Empty;

    private EmploymentContract(
        Guid id,
        Guid employeeId,
        EmploymentContractType type,
        DateOnly startsOn,
        DateOnly? endsOn,
        decimal monthlySalary,
        string currency,
        string? notes)
    {
        Id = id;
        EmployeeId = employeeId;
        Type = type;
        StartsOn = startsOn;
        EndsOn = endsOn;
        MonthlySalary = monthlySalary;
        Currency = currency;
        Notes = notes;
        Status = EmploymentContractStatus.Active;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    public EmploymentContractType Type { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>
    /// Fim da vigência. Nulo apenas em contratos sem termo, e preenchido
    /// quando um contrato é cessado antes do previsto.
    /// </summary>
    public DateOnly? EndsOn { get; private set; }

    /// <summary>
    /// Remuneração base mensal acordada. <c>decimal</c> e nunca vírgula
    /// flutuante (<c>.claude/standards/persistence.md</c>).
    /// </summary>
    public decimal MonthlySalary { get; private set; }

    /// <summary>
    /// Código ISO 4217 da moeda do salário.
    ///
    /// <para>
    /// Guardado por extenso em vez de assumido: `docs` §5 regista capacidade
    /// multi-moeda AOA/USD/EUR como facto do produto. A conversão entre moedas
    /// é de `finance`, que possui as taxas — `hr` só regista o que foi
    /// acordado, na moeda em que foi acordado.
    /// </para>
    /// </summary>
    public string Currency { get; private set; }

    public string? Notes { get; private set; }

    public EmploymentContractStatus Status { get; private set; }

    /// <summary>
    /// Celebra um contrato. Nasce <see cref="EmploymentContractStatus.Active"/>:
    /// um contrato registado é um contrato em vigor.
    /// </summary>
    /// <exception cref="ArgumentException">Quando os termos são incoerentes.</exception>
    public static EmploymentContract Draw(
        Guid employeeId,
        EmploymentContractType type,
        DateOnly startsOn,
        DateOnly? endsOn,
        decimal monthlySalary,
        string currency,
        string? notes = null)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("Um contrato pertence sempre a um colaborador.", nameof(employeeId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlySalary);

        var normalisedCurrency = NormaliseCurrency(currency);

        // O tipo tem de mandar em alguma coisa, senão é decoração.
        //
        // Um contrato sem termo com data de fim, ou um contrato a termo sem
        // ela, é uma contradição que ninguém detectaria depois: apareceria como
        // uma lista de contratos "a expirar" que nunca expiram, ou o inverso.
        switch (type)
        {
            case EmploymentContractType.Permanent when endsOn is not null:
                throw new ArgumentException(
                    "Um contrato sem termo não tem data de fim. Para o cessar, use Terminate.",
                    nameof(endsOn));

            case EmploymentContractType.FixedTerm when endsOn is null:
            case EmploymentContractType.Freelance when endsOn is null:
                throw new ArgumentException(
                    "Um contrato a termo ou de prestação de serviços exige data de fim.",
                    nameof(endsOn));
        }

        if (endsOn is { } fim && fim <= startsOn)
        {
            throw new ArgumentException(
                "A data de fim tem de ser posterior à data de início.",
                nameof(endsOn));
        }

        return new EmploymentContract(
            Guid.CreateVersion7(),
            employeeId,
            type,
            startsOn,
            endsOn,
            monthlySalary,
            normalisedCurrency,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    /// <summary>
    /// Verdadeiro se o contrato está em vigor na data indicada.
    ///
    /// <para>
    /// É por aqui que se responde "qual é o salário desta pessoa hoje" sem
    /// duplicar o valor no Colaborador — que é o que obrigaria a mantê-lo
    /// sincronizado em dois sítios.
    /// </para>
    ///
    /// <para>
    /// <strong>Pergunta temporal, e por isso não consulta o
    /// <see cref="Status"/>.</strong> Cessar preenche <see cref="EndsOn"/>, que
    /// já delimita a vigência. Consultar também o estado faria um contrato
    /// cessado responder "nunca vigorou" — e `payroll` precisa exactamente do
    /// contrário: saber que contrato vigorava em Março para processar Março.
    /// </para>
    /// </summary>
    public bool IsInForceOn(DateOnly date) =>
        date >= StartsOn && (EndsOn is null || date <= EndsOn);

    /// <summary>
    /// Sobrepõe-se no tempo a outro contrato?
    ///
    /// <para>
    /// Usado para impedir duas relações laborais simultâneas com a mesma
    /// pessoa.
    /// </para>
    ///
    /// <para>
    /// <strong>Um contrato cessado continua a ocupar o período que
    /// correu.</strong> Ignorá-lo por estar cessado deixaria celebrar um
    /// contrato retroactivo por cima de meses que já tiveram outro — e
    /// `payroll` encontraria dois contratos em vigor no mesmo mês. O que
    /// liberta o caminho é a data de cessação, que encurta a vigência, não o
    /// estado.
    /// </para>
    /// </summary>
    public bool OverlapsWith(DateOnly otherStart, DateOnly? otherEnd)
    {
        // Sem data de fim, o intervalo estende-se indefinidamente — daí o
        // DateOnly.MaxValue em vez de um caso especial por ramo.
        var thisEnd = EndsOn ?? DateOnly.MaxValue;
        var thatEnd = otherEnd ?? DateOnly.MaxValue;

        return otherStart <= thisEnd && StartsOn <= thatEnd;
    }

    /// <summary>
    /// Cessa o contrato numa data. Serve tanto a chegada ao termo como a
    /// rescisão antecipada — a diferença fica na data, não em dois estados.
    /// </summary>
    public void Terminate(DateOnly on)
    {
        if (Status != EmploymentContractStatus.Active)
        {
            throw new InvalidOperationException("Só um contrato activo pode ser cessado.");
        }

        if (on < StartsOn)
        {
            throw new ArgumentException(
                "Um contrato não pode cessar antes de começar.",
                nameof(on));
        }

        EndsOn = on;
        Status = EmploymentContractStatus.Terminated;
    }

    /// <summary>
    /// Actualiza a remuneração base acordada.
    ///
    /// <para>
    /// <strong>Altera o contrato em vigor, e não guarda histórico de
    /// vencimentos.</strong> Registar a progressão salarial ao longo do tempo é
    /// uma capacidade diferente — e, a existir, é de `payroll`, que é quem
    /// possui o que foi efectivamente pago em cada período. Aqui vive o que
    /// está acordado agora.
    /// </para>
    /// </summary>
    public void ReviseSalary(decimal monthlySalary, string currency)
    {
        if (Status != EmploymentContractStatus.Active)
        {
            throw new InvalidOperationException("Só um contrato activo pode ser revisto.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlySalary);

        MonthlySalary = monthlySalary;
        Currency = NormaliseCurrency(currency);
    }

    private static string NormaliseCurrency(string currency)
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

public enum EmploymentContractType
{
    /// <summary>Sem termo. Não tem data de fim prevista.</summary>
    Permanent,

    /// <summary>A termo certo. Exige data de fim.</summary>
    FixedTerm,

    /// <summary>Prestação de serviços. Exige data de fim.</summary>
    Freelance,
}

public enum EmploymentContractStatus
{
    /// <summary>Em vigor.</summary>
    Active,

    /// <summary>Cessado, por termo ou por rescisão. Fica como histórico.</summary>
    Terminated,
}
