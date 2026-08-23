namespace Rivo.Hr.Domain;

/// <summary>
/// Assiduidade de um Colaborador num dia — a marcação de ponto.
///
/// <para>
/// <strong>É registo, não cálculo.</strong> `hr` possui o que aconteceu: a que
/// horas se entrou e se saiu. Converter isso em horas pagas, subsídio de turno
/// ou desconto por falta é de `payroll` (<c>.claude/modules/payroll.md</c>),
/// que lê esta assiduidade como entrada.
/// </para>
///
/// <para>
/// Um registo por colaborador e por dia. É a granularidade que a marcação de
/// ponto tem, e é o que torna a consulta "quem faltou esta semana" — a que a
/// fila de RH faz — um simples varrimento por data.
/// </para>
/// </summary>
public sealed class AttendanceRecord
{
    /// <summary>Construtor do EF Core. Não usar no domínio.</summary>
    private AttendanceRecord() { }

    private AttendanceRecord(Guid id, Guid employeeId, DateOnly day, AttendanceStatus status)
    {
        Id = id;
        EmployeeId = employeeId;
        Day = day;
        Status = status;
    }

    public Guid Id { get; private set; }

    public int Version { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>O dia a que a marcação diz respeito.</summary>
    public DateOnly Day { get; private set; }

    /// <summary>Entrada efectiva. Nula numa ausência.</summary>
    public DateTimeOffset? CheckedInAt { get; private set; }

    /// <summary>Saída efectiva. Nula enquanto a pessoa não sair.</summary>
    public DateTimeOffset? CheckedOutAt { get; private set; }

    public AttendanceStatus Status { get; private set; }

    /// <summary>Motivo, obrigatório para justificar uma ausência.</summary>
    public string? Justification { get; private set; }

    /// <summary>
    /// Regista a entrada e abre o dia.
    /// </summary>
    /// <param name="late">
    /// Se a entrada foi depois da hora prevista. Quem decide isto é quem
    /// conhece o horário do colaborador — <strong>e o horário não existe
    /// ainda</strong> (Turnos e escalas não está modelado). Fica como
    /// parâmetro explícito em vez de ser adivinhado aqui, para que o dia em que
    /// os turnos existirem esta assinatura não precise de mudar.
    /// </param>
    public static AttendanceRecord CheckIn(Guid employeeId, DateOnly day, DateTimeOffset at, bool late = false)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A marcação pertence sempre a um colaborador.", nameof(employeeId));
        }

        var record = new AttendanceRecord(
            Guid.CreateVersion7(),
            employeeId,
            day,
            late ? AttendanceStatus.Late : AttendanceStatus.Present)
        {
            CheckedInAt = at,
        };

        return record;
    }

    /// <summary>
    /// Regista uma ausência, com ou sem justificação.
    ///
    /// <para>
    /// Uma falta justificada continua a ser uma falta: o estado distingue-as
    /// porque `payroll` trata-as de forma diferente, e a fila de RH só quer ver
    /// as que ainda não têm justificação.
    /// </para>
    /// </summary>
    public static AttendanceRecord Absent(Guid employeeId, DateOnly day, string? justification = null)
    {
        if (employeeId == Guid.Empty)
        {
            throw new ArgumentException("A marcação pertence sempre a um colaborador.", nameof(employeeId));
        }

        var justified = !string.IsNullOrWhiteSpace(justification);

        return new AttendanceRecord(
            Guid.CreateVersion7(),
            employeeId,
            day,
            justified ? AttendanceStatus.Justified : AttendanceStatus.Absent)
        {
            Justification = justified ? justification!.Trim() : null,
        };
    }

    /// <summary>
    /// Regista a saída e fecha o dia.
    /// </summary>
    public void CheckOut(DateTimeOffset at)
    {
        if (CheckedInAt is null)
        {
            throw new InvalidOperationException(
                "Não há saída sem entrada. Para registar uma falta, use Absent.");
        }

        if (CheckedOutAt is not null)
        {
            throw new InvalidOperationException("A saída deste dia já foi registada.");
        }

        if (at < CheckedInAt)
        {
            throw new ArgumentException("A saída não pode ser anterior à entrada.", nameof(at));
        }

        CheckedOutAt = at;
    }

    /// <summary>
    /// Justifica uma ausência já registada.
    ///
    /// <para>
    /// É a acção que a fila de RH executa sobre uma anomalia. Não se aplica a
    /// um dia presente: justificar uma presença não quer dizer nada.
    /// </para>
    /// </summary>
    public void Justify(string justification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);

        if (Status is not (AttendanceStatus.Absent or AttendanceStatus.Late))
        {
            throw new InvalidOperationException(
                "Só uma falta ou um atraso precisam de justificação.");
        }

        Justification = justification.Trim();
        Status = AttendanceStatus.Justified;
    }

    /// <summary>
    /// Tempo entre a entrada e a saída, quando o dia está fechado.
    ///
    /// <para>
    /// <strong>É a duração observada, não horas pagas.</strong> Não desconta
    /// intervalos nem separa horas extraordinárias — isso depende de regras de
    /// remuneração que são de `payroll`.
    /// </para>
    /// </summary>
    public TimeSpan? ObservedDuration =>
        CheckedInAt is { } entrada && CheckedOutAt is { } saida
            ? saida - entrada
            : null;

    /// <summary>
    /// Uma anomalia é o que a fila de RH mostra: falta ou atraso ainda sem
    /// justificação.
    /// </summary>
    public bool IsAnomaly => Status is AttendanceStatus.Absent or AttendanceStatus.Late;
}

public enum AttendanceStatus
{
    Present,
    Late,
    Absent,

    /// <summary>Ausência ou atraso com justificação aceite.</summary>
    Justified,
}
