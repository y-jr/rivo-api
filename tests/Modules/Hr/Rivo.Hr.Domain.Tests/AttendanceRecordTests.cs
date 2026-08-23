using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Invariantes da marcação de ponto.
///
/// <para>
/// O que se protege aqui é a coerência do par entrada/saída — a sequência que,
/// se se partir, faz `payroll` calcular horas negativas sem ninguém perceber
/// porquê.
/// </para>
/// </summary>
public class AttendanceRecordTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 8, 24);
    private static readonly DateTimeOffset Morning = new(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Evening = new(2026, 8, 24, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CheckIn_OpensTheDayAsPresent()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning);

        Assert.Equal(AttendanceStatus.Present, record.Status);
        Assert.Equal(Morning, record.CheckedInAt);
        Assert.Null(record.CheckedOutAt);
        Assert.False(record.IsAnomaly);
    }

    [Fact]
    public void CheckIn_WhenLate_IsAnAnomaly()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning, late: true);

        Assert.Equal(AttendanceStatus.Late, record.Status);
        Assert.True(record.IsAnomaly);
    }

    [Fact]
    public void CheckOut_ClosesTheDay()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning);

        record.CheckOut(Evening);

        Assert.Equal(Evening, record.CheckedOutAt);
        Assert.Equal(TimeSpan.FromHours(9), record.ObservedDuration);
    }

    /// <summary>
    /// Sem entrada não há saída. É o que impede um registo que daria duração
    /// impossível de interpretar a jusante.
    /// </summary>
    [Fact]
    public void CheckOut_WithoutCheckIn_IsRejected()
    {
        var record = AttendanceRecord.Absent(Employee, Day);

        Assert.Throws<InvalidOperationException>(() => record.CheckOut(Evening));
    }

    [Fact]
    public void CheckOut_Twice_IsRejected()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning);
        record.CheckOut(Evening);

        Assert.Throws<InvalidOperationException>(() => record.CheckOut(Evening.AddHours(1)));
    }

    [Fact]
    public void CheckOut_BeforeCheckIn_IsRejected()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Evening);

        Assert.Throws<ArgumentException>(() => record.CheckOut(Morning));
    }

    [Fact]
    public void ObservedDuration_IsNullWhileTheDayIsOpen()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning);

        Assert.Null(record.ObservedDuration);
    }

    [Fact]
    public void Absent_WithoutJustification_IsAnAnomaly()
    {
        var record = AttendanceRecord.Absent(Employee, Day);

        Assert.Equal(AttendanceStatus.Absent, record.Status);
        Assert.True(record.IsAnomaly);
        Assert.Null(record.Justification);
    }

    [Fact]
    public void Absent_WithJustification_IsNotAnAnomaly()
    {
        var record = AttendanceRecord.Absent(Employee, Day, "Atestado médico");

        Assert.Equal(AttendanceStatus.Justified, record.Status);
        Assert.False(record.IsAnomaly);
    }

    /// <summary>
    /// Justificar é a acção que a fila de RH executa sobre uma anomalia — e é
    /// o que a faz sair da fila.
    /// </summary>
    [Fact]
    public void Justify_ClearsTheAnomaly()
    {
        var record = AttendanceRecord.Absent(Employee, Day);

        record.Justify("Falecimento na família");

        Assert.Equal(AttendanceStatus.Justified, record.Status);
        Assert.False(record.IsAnomaly);
        Assert.Equal("Falecimento na família", record.Justification);
    }

    [Fact]
    public void Justify_APresentDay_IsRejected()
    {
        var record = AttendanceRecord.CheckIn(Employee, Day, Morning);

        Assert.Throws<InvalidOperationException>(() => record.Justify("Sem sentido"));
    }

    [Fact]
    public void Justify_WithoutReason_IsRejected()
    {
        var record = AttendanceRecord.Absent(Employee, Day);

        Assert.Throws<ArgumentException>(() => record.Justify("   "));
    }
}
