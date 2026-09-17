using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// As correcções de `hr` (ADR-063).
///
/// <para>
/// O que estes testes protegem, mais do que o mapeamento: que uma correcção
/// <strong>deixa rasto com o valor anterior</strong>, e que uma não-alteração
/// não grava nem audita nada. A segunda parece detalhe e não é — sem ela, abrir
/// um formulário e carregar em «guardar» sem tocar em nada enche a trilha de
/// entradas que dizem que algo mudou quando nada mudou.
/// </para>
/// </summary>
public class CorrectOrganisationTests
{
    private static readonly AuditContext Contexto =
        new(Guid.NewGuid(), "192.0.2.10", Guid.NewGuid().ToString());

    // --- Colaborador ---

    [Fact]
    public async Task CorrectEmployee_NomeNovo_CorrigeEAuditaComOAnterior()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Slva");

        var resultado = await new CorrectEmployee(store, trilha)
            .ExecuteAsync(colaborador.Id, "Ana Silva", Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal("Ana Silva", colaborador.FullName);
        Assert.Equal(1, store.Gravacoes);

        var registo = Assert.Single(trilha.Registos);
        Assert.Equal("hr.employee.corrected", registo.Action);
        Assert.Contains("Ana Slva", registo.PreviousValue);
        Assert.Contains("Ana Silva", registo.NewValue);
    }

    [Fact]
    public async Task CorrectEmployee_MesmoNome_NaoGravaNemAudita()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Silva");

        var resultado = await new CorrectEmployee(store, trilha)
            .ExecuteAsync(colaborador.Id, "Ana Silva", Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
        Assert.Empty(trilha.Registos);
    }

    [Fact]
    public async Task CorrectEmployee_NomeVazio_RecusaSemGravar()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var colaborador = store.Admitir("Ana Silva");

        var resultado = await new CorrectEmployee(store, trilha)
            .ExecuteAsync(colaborador.Id, "   ", Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Rejected, resultado.Outcome);
        Assert.Equal("Ana Silva", colaborador.FullName);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task CorrectEmployee_ColaboradorInexistente_NaoEncontrado()
    {
        var store = new FakeHrStore();

        var resultado = await new CorrectEmployee(store, new FakeAuditTrail())
            .ExecuteAsync(Guid.NewGuid(), "Quem Quer Que Seja", Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.NotFound, resultado.Outcome);
    }

    // --- Transferência ---

    [Fact]
    public async Task TransferEmployee_ParaDepartamentoConhecido_MoveEAudita()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var origem = store.CriarDepartamento("Operações");
        var destino = store.CriarDepartamento("Financeiro");
        var colaborador = store.Admitir("Ana Silva");
        colaborador.MoveToDepartment(origem.Id);

        var resultado = await new TransferEmployee(store, trilha)
            .ExecuteAsync(colaborador.Id, destino.Id, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal(destino.Id, colaborador.DepartmentId);

        var registo = Assert.Single(trilha.Registos);

        // Acção própria: quem lê a trilha distingue uma reorganização de um
        // engano de digitação sem ter de comparar valores.
        Assert.Equal("hr.employee.transferred", registo.Action);
        Assert.Contains(origem.Id.ToString(), registo.PreviousValue);
        Assert.Contains(destino.Id.ToString(), registo.NewValue);
    }

    [Fact]
    public async Task TransferEmployee_DepartamentoInexistente_RecusaSemMover()
    {
        var store = new FakeHrStore();
        var actual = store.CriarDepartamento("Operações");
        var colaborador = store.Admitir("Ana Silva");
        colaborador.MoveToDepartment(actual.Id);

        var resultado = await new TransferEmployee(store, new FakeAuditTrail())
            .ExecuteAsync(colaborador.Id, Guid.NewGuid(), Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Rejected, resultado.Outcome);
        Assert.Equal(actual.Id, colaborador.DepartmentId);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task TransferEmployee_ParaNenhum_EscolhaValida()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var departamento = store.CriarDepartamento("Operações");
        var colaborador = store.Admitir("Ana Silva");
        colaborador.MoveToDepartment(departamento.Id);

        var resultado = await new TransferEmployee(store, trilha)
            .ExecuteAsync(colaborador.Id, departmentId: null, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Null(colaborador.DepartmentId);
        Assert.Contains("null", Assert.Single(trilha.Registos).NewValue);
    }

    // --- Departamento ---

    [Fact]
    public async Task CorrectDepartment_NomeEResponsavel_CorrigeOsDois()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var departamento = store.CriarDepartamento("Operacoes");
        var responsavel = store.Admitir("Ana Silva");

        var resultado = await new CorrectDepartment(store, trilha)
            .ExecuteAsync(departamento.Id, "Operações", responsavel.Id, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal("Operações", departamento.Name);
        Assert.Equal(responsavel.Id, departamento.ManagerId);
        Assert.Equal("hr.department.corrected", Assert.Single(trilha.Registos).Action);
    }

    [Fact]
    public async Task CorrectDepartment_ResponsavelDesconhecido_Recusa()
    {
        var store = new FakeHrStore();
        var departamento = store.CriarDepartamento("Operações");

        var resultado = await new CorrectDepartment(store, new FakeAuditTrail())
            .ExecuteAsync(departamento.Id, "Operações", Guid.NewGuid(), Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Rejected, resultado.Outcome);
        Assert.Null(departamento.ManagerId);
    }

    [Fact]
    public async Task CorrectDepartment_SemAlteracao_NaoGravaNemAudita()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var departamento = store.CriarDepartamento("Operações");

        var resultado = await new CorrectDepartment(store, trilha)
            .ExecuteAsync(departamento.Id, "Operações", managerId: null, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal(0, store.Gravacoes);
        Assert.Empty(trilha.Registos);
    }

    // --- Cargo ---

    [Fact]
    public async Task CorrectPosition_NomeENivel_CorrigeEAudita()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var cargo = store.CriarCargo("Analsta", nivel: 5);

        var resultado = await new CorrectPosition(store, trilha)
            .ExecuteAsync(cargo.Id, "Analista", 4, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Corrected, resultado.Outcome);
        Assert.Equal("Analista", cargo.Name);
        Assert.Equal(4, cargo.HierarchyLevel);

        var registo = Assert.Single(trilha.Registos);
        Assert.Contains("Analsta", registo.PreviousValue);
        Assert.Contains("\"hierarchyLevel\":5", registo.PreviousValue);
        Assert.Contains("\"hierarchyLevel\":4", registo.NewValue);
    }

    [Fact]
    public async Task CorrectPosition_NivelNegativo_Recusa()
    {
        var store = new FakeHrStore();
        var cargo = store.CriarCargo("Analista", nivel: 5);

        var resultado = await new CorrectPosition(store, new FakeAuditTrail())
            .ExecuteAsync(cargo.Id, "Analista", -1, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.Rejected, resultado.Outcome);
        Assert.Equal(5, cargo.HierarchyLevel);
    }

    /// <summary>
    /// A propriedade que interessa mais nesta classe: <strong>corrigir um cargo
    /// nunca lhe muda a autoridade de aprovação.</strong> Se mudasse, ligá-la
    /// num cargo já atribuído daria autoridade a toda a gente que o tem, sem
    /// que nenhuma dessas atribuições passasse pela governança que o BR-20
    /// exige. O caso de uso não tem sequer por onde receber esse valor — este
    /// teste existe para o dia em que alguém pensar em acrescentá-lo.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorrectPosition_NuncaAlteraAMarcaDeAutoridade(bool confereAutoridade)
    {
        var store = new FakeHrStore();
        var cargo = store.CriarCargo("Director", nivel: 1, confereAutoridade: confereAutoridade);

        await new CorrectPosition(store, new FakeAuditTrail())
            .ExecuteAsync(cargo.Id, "Director-Geral", 1, Contexto, CancellationToken.None);

        Assert.Equal("Director-Geral", cargo.Name);
        Assert.Equal(confereAutoridade, cargo.GrantsApprovalAuthority);
    }

    [Fact]
    public async Task CorrectPosition_CargoInexistente_NaoEncontrado()
    {
        var resultado = await new CorrectPosition(new FakeHrStore(), new FakeAuditTrail())
            .ExecuteAsync(Guid.NewGuid(), "Analista", 5, Contexto, CancellationToken.None);

        Assert.Equal(CorrectionOutcome.NotFound, resultado.Outcome);
    }
}
