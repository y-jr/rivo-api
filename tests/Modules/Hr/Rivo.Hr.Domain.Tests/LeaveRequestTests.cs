using Rivo.Hr.Domain;

namespace Rivo.Hr.Domain.Tests;

/// <summary>
/// Invariantes do pedido de férias.
///
/// <para>
/// A que mais importa é a mesma de BR-20 noutra roupa: <strong>pendente não é
/// ausência</strong>. Um pedido à espera de decisão não pode contar como
/// ausência efectiva, ou a decisão deixaria de significar alguma coisa.
/// </para>
/// </summary>
public class LeaveRequestTests
{
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateOnly End = new(2026, 9, 15);

    private static LeaveRequest Draft() =>
        LeaveRequest.Draft(Employee, LeaveType.Annual, Start, End, "Férias de Verão");

    [Fact]
    public void Draft_StartsPending()
    {
        var request = Draft();

        Assert.Equal(LeaveStatus.Pending, request.Status);
        Assert.Null(request.ApprovalRequestId);
    }

    [Fact]
    public void Draft_WithEndBeforeStart_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LeaveRequest.Draft(Employee, LeaveType.Annual, End, Start));
    }

    [Fact]
    public void Draft_ForASingleDay_IsAllowed()
    {
        var request = LeaveRequest.Draft(Employee, LeaveType.Sick, Start, Start);

        Assert.Equal(1, request.CalendarDays);
    }

    /// <summary>
    /// Dias de calendário, extremos incluídos — 1 a 15 de Setembro são 15 dias.
    /// Não são dias úteis: descontar feriados exigiria um calendário de Angola
    /// que não existe no sistema.
    /// </summary>
    [Fact]
    public void CalendarDays_CountsBothEnds()
    {
        Assert.Equal(15, Draft().CalendarDays);
    }

    /// <summary>
    /// <strong>Pendente não é ausência.</strong> É o mesmo princípio de BR-20:
    /// a decisão é que produz o efeito.
    /// </summary>
    [Fact]
    public void CoversDate_WhilePending_IsFalse()
    {
        var request = Draft();

        Assert.False(request.CoversDate(new DateOnly(2026, 9, 5)));
    }

    [Fact]
    public void CoversDate_AfterApproval_CoversTheWholeRange()
    {
        var request = Draft();
        request.Approve();

        Assert.False(request.CoversDate(Start.AddDays(-1)));
        Assert.True(request.CoversDate(Start));
        Assert.True(request.CoversDate(End));
        Assert.False(request.CoversDate(End.AddDays(1)));
    }

    [Fact]
    public void CoversDate_AfterRefusal_IsFalse()
    {
        var request = Draft();
        request.Refuse();

        Assert.False(request.CoversDate(new DateOnly(2026, 9, 5)));
    }

    /// <summary>
    /// Dois pedidos pendentes sobrepostos, aprovados os dois, dariam um
    /// colaborador ausente duas vezes ao mesmo tempo.
    /// </summary>
    [Fact]
    public void OverlapsWith_PendingRequest_Collides()
    {
        var request = Draft();

        Assert.True(request.OverlapsWith(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20)));
        Assert.True(request.OverlapsWith(new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 2)));
        Assert.False(request.OverlapsWith(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 30)));
        Assert.False(request.OverlapsWith(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
    }

    /// <summary>
    /// O que se impede é ausência a dobrar, não histórico: um pedido recusado
    /// ou retirado liberta o período.
    /// </summary>
    [Theory]
    [InlineData(LeaveStatus.Refused)]
    [InlineData(LeaveStatus.Cancelled)]
    public void OverlapsWith_ClosedRequest_NeverCollides(LeaveStatus status)
    {
        var request = Draft();

        if (status == LeaveStatus.Refused)
        {
            request.Refuse();
        }
        else
        {
            request.Cancel();
        }

        Assert.False(request.OverlapsWith(Start, End));
    }

    [Fact]
    public void Approve_Twice_IsRejected()
    {
        var request = Draft();
        request.Approve();

        Assert.Throws<InvalidOperationException>(request.Approve);
    }

    [Fact]
    public void Refuse_AfterApproval_IsRejected()
    {
        var request = Draft();
        request.Approve();

        Assert.Throws<InvalidOperationException>(request.Refuse);
    }

    /// <summary>
    /// Reverter férias já concedidas é decisão de gestão, e passaria por
    /// governança como qualquer outra — não por este método.
    /// </summary>
    [Fact]
    public void Cancel_AfterApproval_IsRejected()
    {
        var request = Draft();
        request.Approve();

        Assert.Throws<InvalidOperationException>(request.Cancel);
    }

    [Fact]
    public void LinkToApprovalRequest_OnClosedRequest_IsRejected()
    {
        var request = Draft();
        request.Cancel();

        Assert.Throws<InvalidOperationException>(() =>
            request.LinkToApprovalRequest(Guid.CreateVersion7()));
    }
}
