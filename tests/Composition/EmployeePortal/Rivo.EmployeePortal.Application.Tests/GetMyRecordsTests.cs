using Rivo.Hr.Contracts;

namespace Rivo.EmployeePortal.Application.Tests;

/// <summary>
/// As três leituras do próprio (ADR-042).
///
/// <para>
/// <strong>O que estes testes provam não é que a lista vem.</strong> É que a
/// lista vem <em>da pessoa certa</em> — o portal resolve o colaborador a
/// partir da conta autenticada e nunca de um identificador que lhe dêem. Um
/// portal que perguntasse pelo colaborador errado devolveria dados
/// igualmente bem formados, e é essa a falha que estes casos apanham.
/// </para>
/// </summary>
public class GetMyRecordsTests
{
    private static EmployeeReference Colaborador(Guid employeeId, Guid userId) =>
        new(employeeId, "Ana Silva", EmployeeStatus.Active, null, null, userId);

    [Fact]
    public async Task Assiduidade_PedeAoHrOColaboradorDaConta()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var outroColaborador = Guid.NewGuid();

        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(employeeId, userId))
            .WithEmployee(Guid.NewGuid(), Colaborador(outroColaborador, Guid.NewGuid()));

        var self = new FakeEmployeeSelfService();
        var useCase = new GetMyAttendance(directory, self);

        await useCase.ExecuteAsync(
            userId,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        // A asserção que importa: o colaborador pedido é o da conta, e não o
        // outro que existe no directório.
        Assert.Equal(employeeId, self.EmployeeIdPedido);
        Assert.NotEqual(outroColaborador, self.EmployeeIdPedido);
    }

    [Fact]
    public async Task Assiduidade_PassaAJanelaTalComoRecebida()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(Guid.NewGuid(), userId));
        var self = new FakeEmployeeSelfService();

        await new GetMyAttendance(directory, self).ExecuteAsync(
            userId,
            new DateOnly(2026, 3, 5),
            new DateOnly(2026, 4, 10),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 3, 5), self.DeQuePediu);
        Assert.Equal(new DateOnly(2026, 4, 10), self.AtePediu);
    }

    [Fact]
    public async Task Assiduidade_JanelaInvertida_ERecusadaSemChegarAoHr()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(Guid.NewGuid(), userId));
        var self = new FakeEmployeeSelfService();

        var resultado = await new GetMyAttendance(directory, self).ExecuteAsync(
            userId,
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 1),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Rejected, resultado.Outcome);
        Assert.NotNull(resultado.Error);

        // E não chegou a perguntar nada a `hr` — recusar depois de ler seria
        // ler à toa.
        Assert.Null(self.EmployeeIdPedido);
    }

    [Fact]
    public async Task Assiduidade_ContaSemVinculo_NaoLeNada()
    {
        var self = new FakeEmployeeSelfService();

        var resultado = await new GetMyAttendance(new FakeEmployeeDirectory(), self).ExecuteAsync(
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, resultado.Outcome);
        Assert.Null(self.EmployeeIdPedido);
    }

    [Fact]
    public async Task Ferias_DevolveOsPedidosDoColaboradorDaConta()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(employeeId, userId));

        var self = new FakeEmployeeSelfService
        {
            Leave =
            [
                new OwnLeaveRequest(
                    Guid.NewGuid(), "Annual", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
                    15, "Approved", null, Guid.NewGuid()),
                // Um recusado, de propósito: quem consulta as próprias férias
                // quer saber o que pediu, e não só o que lhe foi concedido.
                new OwnLeaveRequest(
                    Guid.NewGuid(), "Annual", new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 31),
                    12, "Refused", "Período de fecho", null),
            ],
        };

        var resultado = await new GetMyLeave(directory, self).ExecuteAsync(
            userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, resultado.Outcome);
        Assert.Equal(employeeId, self.EmployeeIdPedido);
        Assert.Equal(2, resultado.Records!.Count);
        Assert.Contains(resultado.Records, l => l.Status == "Refused");
    }

    [Fact]
    public async Task Ferias_ContaSemVinculo_DaNotLinked()
    {
        var resultado = await new GetMyLeave(
            new FakeEmployeeDirectory(), new FakeEmployeeSelfService()).ExecuteAsync(
            Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, resultado.Outcome);
        Assert.Null(resultado.Records);
    }

    [Fact]
    public async Task Documentos_DevolveMetadadosDoColaboradorDaConta()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(employeeId, userId));

        var self = new FakeEmployeeSelfService
        {
            Documents =
            [
                new OwnDocument(
                    Guid.NewGuid(), "Contrato", "contrato.pdf", "application/pdf",
                    102_400, DateTimeOffset.UtcNow),
            ],
        };

        var resultado = await new GetMyDocuments(directory, self).ExecuteAsync(
            userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, resultado.Outcome);
        Assert.Equal(employeeId, self.EmployeeIdPedido);
        Assert.Single(resultado.Records!);
        Assert.Equal("contrato.pdf", resultado.Records![0].FileName);
    }

    [Fact]
    public async Task Documentos_ContaSemVinculo_DaNotLinked()
    {
        var resultado = await new GetMyDocuments(
            new FakeEmployeeDirectory(), new FakeEmployeeSelfService()).ExecuteAsync(
            Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, resultado.Outcome);
    }

    [Fact]
    public async Task SemRegistos_DevolveListaVaziaENaoNotLinked()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(Guid.NewGuid(), userId));

        var resultado = await new GetMyLeave(directory, new FakeEmployeeSelfService()).ExecuteAsync(
            userId, DateTimeOffset.UtcNow, CancellationToken.None);

        // «Nunca pediu férias» e «não tem vínculo» dizem coisas diferentes, e
        // o ecrã trata-as de forma diferente — uma é uma lista vazia, a outra
        // é um 403.
        Assert.Equal(MyRecordsOutcome.Found, resultado.Outcome);
        Assert.Empty(resultado.Records!);
    }
}

/// <summary>
/// Os recibos do próprio.
///
/// <para>
/// O filtro de "só folhas aprovadas" **não se testa aqui**: vive no
/// armazenamento de `payroll`, e testá-lo com um duplo provaria só que o duplo
/// devolve o que lhe puseram. O que este ficheiro prova é o que é desta
/// camada — que se pergunta pela pessoa certa.
/// </para>
/// </summary>
public class GetMyPayslipsTests
{
    private static Rivo.Hr.Contracts.EmployeeReference Colaborador(Guid employeeId, Guid userId) =>
        new(employeeId, "Ana Silva", Rivo.Hr.Contracts.EmployeeStatus.Active, null, null, userId);

    [Fact]
    public async Task PedeAoPayrollOColaboradorDaConta()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(employeeId, userId));
        var payroll = new FakePayrollSelfService();

        await new GetMyPayslips(directory, payroll).ExecuteAsync(
            userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(employeeId, payroll.EmployeeIdPedido);
    }

    [Fact]
    public async Task DevolveOsRecibosComOLiquidoEODocumento()
    {
        var userId = Guid.NewGuid();
        var directory = new FakeEmployeeDirectory()
            .WithEmployee(userId, Colaborador(Guid.NewGuid(), userId));

        var documentId = Guid.NewGuid();
        var payroll = new FakePayrollSelfService
        {
            Payslips =
            [
                new Rivo.Payroll.Contracts.OwnPayslip(
                    Guid.NewGuid(), Guid.NewGuid(), 2026, 8,
                    250_000m, 30_000m, 15_000m, 0m, 0m,
                    NetSalary: 238_500m, WithholdingTax: 21_500m,
                    SocialSecurityContribution: 7_500m, DocumentId: documentId),
                // Sem líquido calculado e sem documento — os dois nulos são
                // "ainda não", e não zero. O ecrã tem de os distinguir.
                new Rivo.Payroll.Contracts.OwnPayslip(
                    Guid.NewGuid(), Guid.NewGuid(), 2026, 9,
                    250_000m, 0m, 0m, 0m, 0m,
                    NetSalary: null, WithholdingTax: null,
                    SocialSecurityContribution: null, DocumentId: null),
            ],
        };

        var resultado = await new GetMyPayslips(directory, payroll).ExecuteAsync(
            userId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.Found, resultado.Outcome);
        Assert.Equal(2, resultado.Records!.Count);
        Assert.Equal(documentId, resultado.Records[0].DocumentId);
        Assert.Null(resultado.Records[1].NetSalary);
        Assert.Null(resultado.Records[1].DocumentId);
    }

    [Fact]
    public async Task ContaSemVinculo_NaoLeNada()
    {
        var payroll = new FakePayrollSelfService();

        var resultado = await new GetMyPayslips(
            new FakeEmployeeDirectory(), payroll).ExecuteAsync(
            Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(MyRecordsOutcome.NotLinked, resultado.Outcome);
        Assert.Null(payroll.EmployeeIdPedido);
    }
}
