using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Admissão, e o que ela faz ao histórico do vínculo (ADR-053).
///
/// <para>
/// Estes testes nasceram de um defeito. O histórico foi acrescentado a
/// <c>LinkEmployeeAccount</c> e esquecido em <c>HireEmployee</c> — e o vínculo
/// pode nascer pelos dois caminhos. Quem fosse admitido já com conta ficava
/// fora do histórico, que é pior do que não haver histórico: parece uma
/// resposta completa.
/// </para>
///
/// <para>
/// Apanhou-o a verificação end-to-end, não o compilador nem os testes — porque
/// nada obrigava os dois caminhos a concordar. Estes testes passam a obrigar.
/// </para>
/// </summary>
public class HireEmployeeTests
{
    private static readonly DateTimeOffset Admissao = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    [Fact]
    public async Task Admitir_Com_Conta_Abre_Um_Episodio()
    {
        var store = new FakeHrStore();
        var conta = Guid.NewGuid();
        var quem = Guid.NewGuid();

        var resultado = await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", departmentId: null, conta, Admissao, Actor(quem), CancellationToken.None);

        Assert.True(resultado.Succeeded);

        var episodio = Assert.Single(store.Episodios);
        Assert.Equal(resultado.EmployeeId, episodio.EmployeeId);
        Assert.Equal(conta, episodio.UserId);
        Assert.True(episodio.IsOpen);

        // A data é a da admissão, não a de agora: é quando o vínculo passou a
        // existir, e é a mesma regra que a migração de retroactivo seguiu.
        Assert.Equal(Admissao, episodio.LinkedOn);
        Assert.Equal(quem, episodio.LinkedByUserId);
    }

    [Fact]
    public async Task Admitir_Sem_Conta_Nao_Abre_Episodio()
    {
        var store = new FakeHrStore();

        await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", departmentId: null, userId: null, Admissao,
            Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(store.Episodios);
    }

    [Fact]
    public async Task Admissao_Recusada_Nao_Abre_Episodio()
    {
        var store = new FakeHrStore();
        var conta = Guid.NewGuid();
        store.Admitir("Ja Tem A Conta", conta);

        var resultado = await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", departmentId: null, conta, Admissao,
            Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HireEmployeeOutcome.UserAlreadyLinked, resultado.Outcome);

        // Só o episódio de quem já tinha a conta.
        Assert.Single(store.Episodios);
    }

    /// <summary>
    /// A invariante que o defeito violou, escrita como teste: qualquer
    /// colaborador com conta tem episódio aberto, tenha o vínculo nascido na
    /// admissão ou depois.
    /// </summary>
    [Fact]
    public async Task Os_Dois_Caminhos_Do_Vinculo_Deixam_Ambos_Episodio()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var relogio = new RelogioFixo(Admissao.AddMonths(1));
        var actor = Guid.NewGuid();

        // Caminho 1: na admissão.
        var naAdmissao = await new HireEmployee(store, trilha).ExecuteAsync(
            "Pela Admissao", null, Guid.NewGuid(), Admissao, Actor(actor), CancellationToken.None);

        // Caminho 2: depois, pela rota do ADR-051.
        var depois = await new HireEmployee(store, trilha).ExecuteAsync(
            "Pela Rota", null, userId: null, Admissao, Actor(actor), CancellationToken.None);
        await new LinkEmployeeAccount(store, trilha, relogio).ExecuteAsync(
            depois.EmployeeId!.Value, Guid.NewGuid(), Actor(actor), CancellationToken.None);

        var historico = new GetEmployeeAccountHistory(store);

        Assert.Single((await historico.ExecuteAsync(naAdmissao.EmployeeId!.Value, CancellationToken.None))!);
        Assert.Single((await historico.ExecuteAsync(depois.EmployeeId!.Value, CancellationToken.None))!);
    }
}
