using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Paginação real e aditiva (ADR-068, item #10) nas listagens de `hr`.
///
/// <para>
/// Cada teste prova duas coisas: sem página pedida, a listagem continua a
/// devolver tudo (aditivo); com página, a fatia vem na ordem determinística
/// certa e o total é o da colecção inteira, não da fatia.
/// </para>
/// </summary>
public class PaginationTests
{
    private static readonly Guid Colaborador = Guid.CreateVersion7();

    [Fact]
    public async Task Departamentos_SemPagina_DevolveTodos()
    {
        var store = new FakeHrStore();
        store.CriarDepartamento("Vendas");
        store.CriarDepartamento("Operações");
        store.CriarDepartamento("Finanças");

        var (itens, total) = await new ListDepartments(store).ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(3, itens.Count);
        Assert.Null(total);
    }

    [Fact]
    public async Task Departamentos_ComPagina_DevolveFatiaOrdenadaEOTotal()
    {
        var store = new FakeHrStore();
        store.CriarDepartamento("Vendas");
        store.CriarDepartamento("Operações");
        store.CriarDepartamento("Finanças");

        var (itens, total) = await new ListDepartments(store).ExecuteAsync(
            new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(["Finanças", "Operações"], itens.Select(d => d.Name));
    }

    [Fact]
    public async Task Cargos_ComPagina_RespeitaOrdemPorNivelDepoisNome()
    {
        var store = new FakeHrStore();
        store.CriarCargo("Director", nivel: 1);
        store.CriarCargo("Gestor B", nivel: 2);
        store.CriarCargo("Gestor A", nivel: 2);
        store.CriarCargo("Analista", nivel: 3);

        var (pagina2, total) = await new ListPositions(store).ExecuteAsync(
            new PageRequest(2, 2), CancellationToken.None);

        Assert.Equal(4, total);
        Assert.Equal(["Gestor B", "Analista"], pagina2.Select(p => p.Name));
    }

    [Fact]
    public async Task Ferias_ComPagina_OrdenaPelaDataMaisRecentePrimeiro()
    {
        var store = new FakeHrStore();
        store.AdicionarFerias(LeaveRequest.Draft(
            Colaborador, LeaveType.Annual, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10)));
        store.AdicionarFerias(LeaveRequest.Draft(
            Colaborador, LeaveType.Annual, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5)));
        store.AdicionarFerias(LeaveRequest.Draft(
            Colaborador, LeaveType.Annual, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3)));

        var (itens, total) = await new ListLeave(store).ExecuteAsync(
            Colaborador, new PageRequest(1, 2), CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(
            [new DateOnly(2026, 6, 1), new DateOnly(2026, 3, 1)],
            itens.Select(l => l.StartsOn));
    }

    [Fact]
    public async Task Contratos_SemColaborador_PaginaATabelaInteira()
    {
        var store = new FakeHrStore();
        store.AdicionarContrato(EmploymentContract.Draw(
            Colaborador, EmploymentContractType.Permanent, new DateOnly(2024, 1, 1), null, 100_000m, "AOA"));
        store.AdicionarContrato(EmploymentContract.Draw(
            Guid.CreateVersion7(), EmploymentContractType.Permanent, new DateOnly(2025, 1, 1), null, 150_000m, "AOA"));

        var (itens, total) = await new ListEmploymentContracts(store).ExecuteAsync(
            employeeId: null, new PageRequest(1, 1), CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Single(itens);
        Assert.Equal(new DateOnly(2025, 1, 1), itens[0].StartsOn);
    }

    /// <summary>
    /// O caso que a paginação aditiva existe para não partir: um colaborador
    /// concreto tem o histórico pequeno de propósito (ADR-068), e não pagina —
    /// mesmo que `page`/`pageSize` sejam passados, são ignorados nesse ramo.
    /// </summary>
    [Fact]
    public async Task Contratos_DeUmColaborador_NuncaPagina()
    {
        var store = new FakeHrStore();
        store.AdicionarContrato(EmploymentContract.Draw(
            Colaborador, EmploymentContractType.FixedTerm, new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1), 80_000m, "AOA"));
        store.AdicionarContrato(EmploymentContract.Draw(
            Colaborador, EmploymentContractType.Permanent, new DateOnly(2021, 1, 2), null, 100_000m, "AOA"));

        var (itens, total) = await new ListEmploymentContracts(store).ExecuteAsync(
            Colaborador, new PageRequest(1, 1), CancellationToken.None);

        Assert.Equal(2, itens.Count);
        Assert.Null(total);
    }

    /// <summary>
    /// O caso de correcção real desta ronda: filtrar <c>anomaliesOnly</c>
    /// depois do <c>Skip</c>/<c>Take</c> faria a página 2 saltar registos que a
    /// página 1 nunca chegou a excluir. Este teste falharia com o código
    /// anterior (filtro em memória, depois de paginar).
    /// </summary>
    [Fact]
    public async Task Assiduidade_AnomaliasComPagina_FiltraAntesDePaginar()
    {
        var store = new FakeHrStore();
        var inicio = new DateOnly(2026, 8, 1);

        // Dia 1: presente (não é anomalia). Dias 2 a 5: atraso/falta.
        store.AdicionarMarcacao(AttendanceRecord.CheckIn(Colaborador, inicio, inicio.ToDateTime(TimeOnly.MinValue)));
        store.AdicionarMarcacao(AttendanceRecord.Absent(Colaborador, inicio.AddDays(1)));
        store.AdicionarMarcacao(AttendanceRecord.CheckIn(Colaborador, inicio.AddDays(2), inicio.AddDays(2).ToDateTime(TimeOnly.MinValue), late: true));
        store.AdicionarMarcacao(AttendanceRecord.Absent(Colaborador, inicio.AddDays(3)));
        store.AdicionarMarcacao(AttendanceRecord.Absent(Colaborador, inicio.AddDays(4)));

        var (itens, total) = await new ListAttendance(store).ExecuteAsync(
            inicio, inicio.AddDays(30), Colaborador, anomaliesOnly: true,
            new PageRequest(2, 2), CancellationToken.None);

        // 4 anomalias no total (dias 2,3,4,5) — não 5 (o dia 1 fica de fora do
        // total, prova de que o filtro correu antes da contagem/corte).
        Assert.Equal(4, total);

        // Ordem: mais recente primeiro. Página 1 é dias 5 e 4; página 2 são os
        // dias 3 e 2 — nunca o dia 1 (presente), mesmo por baixo da página.
        Assert.Equal(
            [inicio.AddDays(2), inicio.AddDays(1)],
            itens.Select(a => a.Day));
    }

    [Fact]
    public async Task Assiduidade_SemAnomalias_PaginaTodosOsRegistos()
    {
        var store = new FakeHrStore();
        var inicio = new DateOnly(2026, 8, 1);

        store.AdicionarMarcacao(AttendanceRecord.CheckIn(Colaborador, inicio, inicio.ToDateTime(TimeOnly.MinValue)));
        store.AdicionarMarcacao(AttendanceRecord.Absent(Colaborador, inicio.AddDays(1)));

        var (itens, total) = await new ListAttendance(store).ExecuteAsync(
            inicio, inicio.AddDays(30), Colaborador, anomaliesOnly: false, null, CancellationToken.None);

        Assert.Equal(2, itens.Count);
        Assert.Null(total);
    }
}
