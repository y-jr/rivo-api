using Rivo.Hr.Contracts;

namespace Rivo.EmployeePortal.Application.Tests;

/// <summary>
/// O que estes testes protegem não é o mapeamento — é a resolução de «o
/// próprio». Duas propriedades, repetidas nas quatro leituras:
///
/// <list type="number">
///   <item>o identificador que chega ao módulo é o do <strong>colaborador
///   ligado</strong>, nunca o da conta;</item>
///   <item>sem vínculo <strong>não se lê nada</strong> — o módulo não é sequer
///   chamado, e por isso não há resposta a filtrar depois.</item>
/// </list>
/// </summary>
public class GetMyRecordsTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    private static (GetMyRecords Records, FakeEmployeeSelfService Hr, FakePayrollSelfService Payroll)
        ComVinculo(Guid userId, Guid employeeId)
    {
        var directory = new FakeEmployeeDirectory().WithEmployee(
            userId,
            new EmployeeReference(
                employeeId,
                "Ana Silva",
                EmployeeStatus.Active,
                Guid.NewGuid(),
                new PositionReference(Guid.NewGuid(), "Programadora", GrantsApprovalAuthority: false),
                userId));

        var hr = new FakeEmployeeSelfService();
        var payroll = new FakePayrollSelfService();

        return (new GetMyRecords(directory, hr, payroll), hr, payroll);
    }

    private static (GetMyRecords Records, FakeEmployeeSelfService Hr, FakePayrollSelfService Payroll) SemVinculo()
    {
        var hr = new FakeEmployeeSelfService();
        var payroll = new FakePayrollSelfService();

        return (new GetMyRecords(new FakeEmployeeDirectory(), hr, payroll), hr, payroll);
    }

    [Fact]
    public async Task AttendanceAsync_ContaLigada_LeComOIdDoColaborador()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (records, hr, _) = ComVinculo(userId, employeeId);

        var result = await records.AttendanceAsync(
            userId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16), Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, result.Outcome);
        Assert.Single(result.Records);

        // A propriedade que importa: o módulo recebeu o colaborador, não a conta.
        Assert.Equal([employeeId], hr.AttendanceCalls);
        Assert.NotEqual(userId, employeeId);
    }

    [Fact]
    public async Task AttendanceAsync_PassaAJanelaPedidaSemAAlterar()
    {
        var userId = Guid.NewGuid();
        var (records, hr, _) = ComVinculo(userId, Guid.NewGuid());
        var de = new DateOnly(2026, 1, 1);
        var ate = new DateOnly(2026, 3, 31);

        await records.AttendanceAsync(userId, de, ate, Agora, CancellationToken.None);

        Assert.Equal((de, ate), hr.UltimaJanela);
    }

    [Fact]
    public async Task LeaveAsync_ContaLigada_LeComOIdDoColaborador()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (records, hr, _) = ComVinculo(userId, employeeId);

        var result = await records.LeaveAsync(userId, Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, result.Outcome);
        Assert.Equal([employeeId], hr.LeaveCalls);
    }

    [Fact]
    public async Task DocumentsAsync_ContaLigada_LeComOIdDoColaborador()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (records, hr, _) = ComVinculo(userId, employeeId);

        var result = await records.DocumentsAsync(userId, Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, result.Outcome);
        Assert.Equal([employeeId], hr.DocumentCalls);
    }

    [Fact]
    public async Task PayslipsAsync_ContaLigada_LeComOIdDoColaborador()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var (records, _, payroll) = ComVinculo(userId, employeeId);

        var result = await records.PayslipsAsync(userId, Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, result.Outcome);
        Assert.Equal([employeeId], payroll.PayslipCalls);
    }

    /// <summary>
    /// Sem vínculo, as quatro recusam <strong>antes</strong> de ler. Um teste por
    /// leitura e não um só, porque o defeito que isto apanha é exactamente o de
    /// uma delas passar a ler primeiro e filtrar depois.
    /// </summary>
    [Fact]
    public async Task SemVinculo_AsQuatroRecusamSemChamarOsModulos()
    {
        var userId = Guid.NewGuid();
        var (records, hr, payroll) = SemVinculo();

        var assiduidade = await records.AttendanceAsync(
            userId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16), Agora, CancellationToken.None);
        var ferias = await records.LeaveAsync(userId, Agora, CancellationToken.None);
        var documentos = await records.DocumentsAsync(userId, Agora, CancellationToken.None);
        var recibos = await records.PayslipsAsync(userId, Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, assiduidade.Outcome);
        Assert.Equal(MyRecordsOutcome.NotLinked, ferias.Outcome);
        Assert.Equal(MyRecordsOutcome.NotLinked, documentos.Outcome);
        Assert.Equal(MyRecordsOutcome.NotLinked, recibos.Outcome);

        Assert.Empty(hr.AttendanceCalls);
        Assert.Empty(hr.LeaveCalls);
        Assert.Empty(hr.DocumentCalls);
        Assert.Empty(payroll.PayslipCalls);
    }

    [Fact]
    public async Task SemVinculo_ARespostaVemVaziaENaoNula()
    {
        var (records, _, _) = SemVinculo();

        var result = await records.PayslipsAsync(Guid.NewGuid(), Agora, CancellationToken.None);

        // O handler devolve 403 e não o corpo, mas uma lista nula aqui seria uma
        // NullReferenceException à espera de quem tratasse este desfecho como dados.
        Assert.NotNull(result.Records);
        Assert.Empty(result.Records);
    }

    /// <summary>
    /// A conta de outra pessoa não vê os registos desta. Parece trivial e é —
    /// mas é a razão de nenhum destes métodos aceitar um <c>employeeId</c>, e
    /// vale tê-lo escrito no dia em que alguém pensar em acrescentar um.
    /// </summary>
    [Fact]
    public async Task ContaDeOutroUtilizador_NaoAlcancaOsRegistosDesteColaborador()
    {
        var userId = Guid.NewGuid();
        var (records, hr, _) = ComVinculo(userId, Guid.NewGuid());

        var result = await records.DocumentsAsync(Guid.NewGuid(), Agora, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, result.Outcome);
        Assert.Empty(hr.DocumentCalls);
    }
}
