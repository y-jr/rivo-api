using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Admissão.
///
/// <para>
/// Estes testes nasceram de um defeito — o histórico do vínculo tinha sido
/// acrescentado a <c>LinkEmployeeAccount</c> e esquecido aqui (ADR-053) — e
/// mudaram de propósito no dia seguinte, quando o ADR-054 tirou o
/// <c>userId</c> da admissão. Passaram de «admitir com conta abre um episódio»
/// a <strong>«admitir não cria vínculo nenhum»</strong>, que é a garantia que
/// interessa agora.
/// </para>
///
/// <para>
/// A conversão é o próprio argumento do ADR-054: o defeito só era possível
/// porque o vínculo podia nascer por dois caminhos. Passou a nascer por um.
/// </para>
/// </summary>
public class HireEmployeeTests
{
    private static readonly DateTimeOffset Admissao = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static AuditContext Actor(Guid quem) => new(quem, null, null);

    [Fact]
    public async Task Admitir_Cria_Colaborador_Sem_Conta()
    {
        var store = new FakeHrStore();

        var resultado = await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", departmentId: null, Admissao, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.True(resultado.Succeeded);
        Assert.Equal(HireEmployeeOutcome.Hired, resultado.Outcome);
    }

    /// <summary>
    /// A garantia central do ADR-054. Enquanto a admissão aceitou
    /// <c>userId</c>, o perfil HR podia criar aqui o vínculo que o ADR-051 lhe
    /// tinha recusado reatribuir — uma porta trancada ao lado de uma janela
    /// aberta.
    /// </summary>
    [Fact]
    public async Task Admitir_Nunca_Abre_Episodio_De_Vinculo()
    {
        var store = new FakeHrStore();

        var resultado = await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", departmentId: null, Admissao, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(store.Episodios);

        // E o colaborador nasce sem conta, não com uma conta qualquer.
        var historico = await new GetEmployeeAccountHistory(store)
            .ExecuteAsync(resultado.EmployeeId!.Value, CancellationToken.None);
        Assert.Empty(historico!);
    }

    [Fact]
    public async Task Departamento_Inexistente_E_Recusado()
    {
        var store = new FakeHrStoreSemDepartamentos();

        var resultado = await new HireEmployee(store, new FakeAuditTrail()).ExecuteAsync(
            "Ana Bento", Guid.NewGuid(), Admissao, Actor(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HireEmployeeOutcome.DepartmentNotFound, resultado.Outcome);
    }

    /// <summary>
    /// Só há um caminho para o vínculo nascer, e é o do ADR-051. Depois da
    /// admissão, e com a permissão dedicada.
    /// </summary>
    [Fact]
    public async Task O_Vinculo_So_Nasce_Pela_Rota_Dedicada()
    {
        var store = new FakeHrStore();
        var trilha = new FakeAuditTrail();
        var relogio = new RelogioFixo(Admissao.AddMonths(1));
        var actor = Guid.NewGuid();

        var admitido = await new HireEmployee(store, trilha).ExecuteAsync(
            "Ana Bento", null, Admissao, Actor(actor), CancellationToken.None);

        Assert.Empty(store.Episodios);

        await new LinkEmployeeAccount(store, trilha, relogio).ExecuteAsync(
            admitido.EmployeeId!.Value, Guid.NewGuid(), Actor(actor), CancellationToken.None);

        var episodio = Assert.Single(store.Episodios);
        Assert.Equal(admitido.EmployeeId, episodio.EmployeeId);

        // A data é a da ligação, não a da admissão: o vínculo passou a existir
        // depois, e datá-lo da admissão seria dizer que aquela conta podia agir
        // por aquela pessoa num período em que não podia.
        Assert.Equal(Admissao.AddMonths(1), episodio.LinkedOn);
    }
}

/// <summary>Dobra que recusa qualquer departamento, para o caminho do 404.</summary>
internal sealed class FakeHrStoreSemDepartamentos : HrStoreParcial
{
    public override Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
