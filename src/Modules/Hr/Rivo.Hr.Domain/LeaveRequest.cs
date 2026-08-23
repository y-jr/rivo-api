namespace Rivo.Hr.Domain;

/// <summary>
/// Pedido de férias ou de outra ausência planeada.
///
/// <para>
/// <strong>Não tem saldo, e é omissão deliberada.</strong>
/// <c>.claude/modules/hr.md</c> regista as regras de férias — acumulação,
/// saldo, carry-over — como <em>não detalhadas em `docs`</em>. Implementar um
/// contador de dias disponíveis obrigaria a inventar a política de direito a
/// férias, que é matéria de lei laboral e de contrato, não de suposição.
/// </para>
///
/// <para>
/// <strong>Não tem passos de aprovação próprios.</strong> `modules/hr.md`
/// proíbe-o expressamente: a decisão vive em `approval`, e aqui guarda-se
/// apenas o identificador do processo e o seu desfecho. É a correcção directa
/// ao anti-padrão do protótipo, onde `approval_steps` estava preso a
/// `employee_requests`.
/// </para>
/// </summary>
public sealed class LeaveRequest
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private LeaveRequest() { }

    private LeaveRequest(
        Guid id,
        Guid employeeId,
        LeaveType type,
        DateOnly startsOn,
        DateOnly endsOn,
        string? reason)
    {
        Id = id;
        EmployeeId = employeeId;
        Type = type;
        StartsOn = startsOn;
        EndsOn = endsOn;
        Reason = reason;
        Status = LeaveStatus.Pending;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    public LeaveType Type { get; private set; }

    public DateOnly StartsOn { get; private set; }

    /// <summary>Último dia de ausência, <strong>inclusive</strong>.</summary>
    public DateOnly EndsOn { get; private set; }

    public string? Reason { get; private set; }

    public LeaveStatus Status { get; private set; }

    /// <summary>
    /// Processo de aprovação que decide este pedido. Nulo só entre a criação e
    /// a submissão, que acontecem no mesmo caso de uso.
    /// </summary>
    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>
    /// Dias de calendário abrangidos, extremos incluídos.
    ///
    /// <para>
    /// <strong>Dias de calendário, e não dias úteis.</strong> Descontar fins de
    /// semana e feriados exige um calendário de feriados de Angola, que não
    /// existe no sistema — e inventá-lo daria um número errado com ar de
    /// certo. Quem precisar de dias úteis para efeitos de remuneração é
    /// `payroll`, que é quem possui essas regras.
    /// </para>
    /// </summary>
    public int CalendarDays => EndsOn.DayNumber - StartsOn.DayNumber + 1;

    public static LeaveRequest Draft(
        Guid employeeId,
        LeaveType type,
        DateOnly startsOn,
        DateOnly endsOn,
        string? reason = null)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("O pedido pertence sempre a um colaborador.", nameof(employeeId));
        }

        if (endsOn < startsOn)
        {
            throw new ArgumentException(
                "O último dia de ausência não pode ser anterior ao primeiro.",
                nameof(endsOn));
        }

        return new LeaveRequest(
            Guid.CreateVersion7(),
            employeeId,
            type,
            startsOn,
            endsOn,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    /// <summary>
    /// Liga o pedido ao processo de aprovação submetido.
    /// </summary>
    public void LinkToApprovalRequest(Guid requestId)
    {
        if (Status != LeaveStatus.Pending)
        {
            throw new InvalidOperationException(
                "Só um pedido pendente está ligado a um processo de aprovação.");
        }

        ApprovalRequestId = requestId;
    }

    /// <summary>
    /// Sobrepõe-se no tempo a outro período?
    ///
    /// <para>
    /// Um pedido recusado ou cancelado <strong>não colide</strong>: o que se
    /// impede é ausência a dobrar, não histórico. Um pendente colide — duas
    /// ausências sobrepostas à espera de decisão dariam, aprovadas as duas, um
    /// colaborador ausente duas vezes ao mesmo tempo.
    /// </para>
    /// </summary>
    public bool OverlapsWith(DateOnly otherStart, DateOnly otherEnd)
    {
        if (Status is LeaveStatus.Refused or LeaveStatus.Cancelled)
        {
            return false;
        }

        return otherStart <= EndsOn && StartsOn <= otherEnd;
    }

    /// <summary>
    /// Verdadeiro se o colaborador está ausente nesta data.
    ///
    /// <para>
    /// Só um pedido <strong>aprovado</strong> conta. Pendente não é ausência —
    /// é uma intenção à espera de decisão, tal como uma atribuição de cargo
    /// pendente não confere o cargo (BR-20).
    /// </para>
    /// </summary>
    public bool CoversDate(DateOnly date) =>
        Status == LeaveStatus.Approved && date >= StartsOn && date <= EndsOn;

    /// <summary>Aprovado em governança. É o que o torna ausência de facto.</summary>
    public void Approve()
    {
        if (Status != LeaveStatus.Pending)
        {
            throw new InvalidOperationException("Só um pedido pendente pode ser aprovado.");
        }

        Status = LeaveStatus.Approved;
    }

    /// <summary>
    /// Recusado em governança. Conserva-se: que alguém tenha pedido e lhe tenha
    /// sido negado é informação, e apagá-la deixaria a trilha a falar de um
    /// registo que já não existe.
    /// </summary>
    public void Refuse()
    {
        if (Status != LeaveStatus.Pending)
        {
            throw new InvalidOperationException("Só um pedido pendente pode ser recusado.");
        }

        Status = LeaveStatus.Refused;
    }

    /// <summary>
    /// Retirado por quem o pediu, antes de haver decisão.
    ///
    /// <para>
    /// Um pedido já aprovado não se cancela por aqui: reverter férias
    /// concedidas é decisão de gestão, e passaria por governança como qualquer
    /// outra.
    /// </para>
    /// </summary>
    public void Cancel()
    {
        if (Status != LeaveStatus.Pending)
        {
            throw new InvalidOperationException(
                "Só um pedido pendente pode ser retirado.");
        }

        Status = LeaveStatus.Cancelled;
    }
}

/// <summary>
/// Natureza da ausência.
///
/// <para>
/// <strong>Nenhum destes tipos traz regra de remuneração associada</strong> —
/// se uma ausência é paga, e em que medida, é cálculo de `payroll` sobre
/// regras que `docs` não detalha.
/// </para>
/// </summary>
public enum LeaveType
{
    /// <summary>Férias.</summary>
    Annual,

    /// <summary>Baixa médica.</summary>
    Sick,

    /// <summary>Licença parental.</summary>
    Parental,

    /// <summary>Ausência sem retribuição.</summary>
    Unpaid,
}

public enum LeaveStatus
{
    /// <summary>Submetido, à espera de decisão. <strong>Não é ausência ainda.</strong></summary>
    Pending,

    Approved,

    Refused,

    /// <summary>Retirado por quem o pediu, antes de haver decisão.</summary>
    Cancelled,
}
