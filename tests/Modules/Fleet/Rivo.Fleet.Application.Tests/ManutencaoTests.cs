using System.Globalization;
using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.Tests;

/// <summary>
/// Planos de manutenção por vencer, e o fecho de uma manutenção.
/// </summary>
public class ManutencaoTests
{
    private static readonly DateOnly Hoje = new(2026, 6, 15);
    private static readonly TimeProvider Relogio =
        new RelogioFixo(new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero));

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    // ── planos por vencer ──

    /// <summary>
    /// A fronteira da janela é inclusiva: um plano que vence exactamente no
    /// último dia do horizonte entra. É onde um <c>&lt;</c> em vez de
    /// <c>&lt;=</c> passa despercebido, e onde a manutenção do dia seguinte
    /// deixaria de ser avisada.
    /// </summary>
    [Fact]
    public async Task A_Fronteira_Do_Horizonte_E_Inclusiva()
    {
        var store = new FakeVehicleStore();
        var viatura = store.Registar("LD-01-AA");
        viatura.SchedulePlan("Revisão", 180, Hoje.AddDays(30));   // no limite
        viatura.SchedulePlan("Pneus", 180, Hoje.AddDays(31));     // um dia depois

        var vencidos = await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(30, CancellationToken.None);

        Assert.Equal("Revisão", Assert.Single(vencidos).Description);
    }

    /// <summary>
    /// Um plano cancelado não se avisa. O filtro é do caso de uso, e não do
    /// armazenamento — a dobra devolve tudo de propósito.
    /// </summary>
    [Fact]
    public async Task Plano_Cancelado_Nao_Aparece_Mesmo_Estando_Vencido()
    {
        var store = new FakeVehicleStore();
        var viatura = store.Registar("LD-01-AA");
        var plano = viatura.SchedulePlan("Revisão", 180, Hoje.AddDays(-10));
        viatura.CancelPlan(plano.Id);

        var vencidos = await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(30, CancellationToken.None);

        Assert.Empty(vencidos);
    }

    /// <summary>
    /// Um plano já vencido continua a aparecer, e <strong>marcado</strong>.
    /// Desaparecer da lista por ter passado do prazo seria o pior desfecho
    /// possível: a manutenção mais atrasada é a que mais precisa de ser vista.
    /// </summary>
    [Fact]
    public async Task Plano_Ja_Vencido_Aparece_E_Vem_Marcado()
    {
        var store = new FakeVehicleStore();
        var viatura = store.Registar("LD-01-AA");
        viatura.SchedulePlan("Revisão", 180, Hoje.AddDays(-5));

        var vencido = Assert.Single(await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(30, CancellationToken.None));

        Assert.True(vencido.IsOverdue);
    }

    [Fact]
    public async Task Plano_Que_Vence_Hoje_Nao_Esta_Atrasado()
    {
        var store = new FakeVehicleStore();
        var viatura = store.Registar("LD-01-AA");
        viatura.SchedulePlan("Revisão", 180, Hoje);

        var plano = Assert.Single(await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(0, CancellationToken.None));

        // Horizonte zero cobre hoje, e hoje ainda não é atraso.
        Assert.False(plano.IsOverdue);
    }

    /// <summary>
    /// A lista é de planos e não de viaturas: uma viatura com dois planos por
    /// vencer dá duas linhas, e a matrícula repete-se — é o que quem gere a
    /// oficina precisa de ver.
    /// </summary>
    [Fact]
    public async Task Uma_Viatura_Com_Dois_Planos_Da_Duas_Linhas()
    {
        var store = new FakeVehicleStore();
        var viatura = store.Registar("LD-01-AA");
        viatura.SchedulePlan("Revisão", 180, Hoje.AddDays(5));
        viatura.SchedulePlan("Pneus", 365, Hoje.AddDays(10));
        store.Registar("LD-02-BB"); // sem planos, não contribui

        var vencidos = await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(30, CancellationToken.None);

        Assert.Equal(2, vencidos.Count);
        Assert.All(vencidos, p => Assert.Equal("LD-01-AA", p.PlateNumber));
    }

    [Fact]
    public async Task A_Lista_Vem_Ordenada_Pela_Data_De_Vencimento()
    {
        var store = new FakeVehicleStore();
        store.Registar("LD-01-AA").SchedulePlan("Terceiro", 180, Hoje.AddDays(20));
        store.Registar("LD-02-BB").SchedulePlan("Primeiro", 180, Hoje.AddDays(-3));
        store.Registar("LD-03-CC").SchedulePlan("Segundo", 180, Hoje.AddDays(7));

        var vencidos = await new ListDueMaintenancePlans(store, Relogio)
            .ExecuteAsync(30, CancellationToken.None);

        // Ordenado através das viaturas todas, não dentro de cada uma.
        Assert.Equal(["Primeiro", "Segundo", "Terceiro"], vencidos.Select(p => p.Description));
    }

    // ── fecho de manutenção ──

    /// <summary>
    /// <strong>O custo é opcional (ADR-048), e a ausência tem de sair como
    /// ausência.</strong> Uma manutenção fechada sem custo registado não conta
    /// como zero — simplesmente não conta —, e a trilha tem de o dizer com
    /// <c>null</c>, não com <c>0</c>.
    /// </summary>
    [Fact]
    public async Task Fechar_Sem_Custo_Audita_Null_E_Nao_Zero()
    {
        var store = new FakeVehicleStore();
        var trilha = new FakeAuditTrail();
        var viatura = store.Registar("LD-01-AA");
        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje.AddDays(-2));

        var resultado = await new CloseMaintenance(store, trilha)
            .ExecuteAsync(viatura.Id, registo.Id, Hoje, cost: null, Actor(), CancellationToken.None);

        Assert.Equal(MaintenanceLifecycleOutcome.Closed, resultado);
        Assert.Contains("\"cost\":null", Assert.Single(trilha.Registos).NewValue);
    }

    /// <summary>
    /// O custo vai para a trilha com <c>InvariantCulture</c>, e isso não é
    /// detalhe: em <c>pt-AO</c> o separador decimal é a vírgula, e um
    /// <c>ToString()</c> normal produziria <c>1234,56</c> — que parte o JSON
    /// da trilha, transformando um campo em dois.
    /// </summary>
    [Fact]
    public async Task O_Custo_Vai_Para_A_Trilha_Com_Ponto_Decimal()
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-PT");   // vírgula decimal

        try
        {
            var store = new FakeVehicleStore();
            var trilha = new FakeAuditTrail();
            var viatura = store.Registar("LD-01-AA");
            var registo = viatura.OpenMaintenance(MaintenanceType.Corrective, "Travões", Hoje.AddDays(-1));

            await new CloseMaintenance(store, trilha)
                .ExecuteAsync(viatura.Id, registo.Id, Hoje, 1234.56m, Actor(), CancellationToken.None);

            var valor = Assert.Single(trilha.Registos).NewValue;
            Assert.Contains("\"cost\":1234.56", valor);
            Assert.DoesNotContain("1234,56", valor);
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    /// <summary>
    /// Viatura inexistente e manutenção inexistente são desfechos distintos, e
    /// a distinção é do caso de uso: o agregado só é consultado depois de a
    /// viatura ser encontrada.
    /// </summary>
    [Fact]
    public async Task Viatura_E_Manutencao_Inexistentes_Distinguem_Se()
    {
        var store = new FakeVehicleStore();
        var trilha = new FakeAuditTrail();
        var viatura = store.Registar("LD-01-AA");
        var caso = new CloseMaintenance(store, trilha);

        Assert.Equal(
            MaintenanceLifecycleOutcome.VehicleNotFound,
            await caso.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Hoje, null, Actor(), CancellationToken.None));

        Assert.Equal(
            MaintenanceLifecycleOutcome.MaintenanceNotFound,
            await caso.ExecuteAsync(viatura.Id, Guid.NewGuid(), Hoje, null, Actor(), CancellationToken.None));

        Assert.Empty(trilha.Registos);
        Assert.Equal(0, store.Gravacoes);
    }

    [Fact]
    public async Task Fechar_Duas_Vezes_E_Recusado_Sem_Segunda_Auditoria()
    {
        var store = new FakeVehicleStore();
        var trilha = new FakeAuditTrail();
        var viatura = store.Registar("LD-01-AA");
        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje.AddDays(-2));
        var caso = new CloseMaintenance(store, trilha);

        await caso.ExecuteAsync(viatura.Id, registo.Id, Hoje, 100m, Actor(), CancellationToken.None);

        var segunda = await caso.ExecuteAsync(
            viatura.Id, registo.Id, Hoje, 200m, Actor(), CancellationToken.None);

        Assert.Equal(MaintenanceLifecycleOutcome.Rejected, segunda);
        Assert.Single(trilha.Registos);
    }
}
