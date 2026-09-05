using System.Globalization;
using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.UseCases;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Application.Tests;

/// <summary>
/// A trilha de auditoria não pode depender da cultura em que o processo corre
/// (ADR-056).
///
/// <para>
/// O <c>NewValue</c> é JSON montado por interpolação de strings, e uma
/// interpolação usa a cultura corrente. Em <c>pt-AO</c> ou <c>pt-PT</c>:
/// </para>
///
/// <list type="bullet">
/// <item>um decimal sai <c>1234,56</c> e <strong>parte o objecto em dois
/// campos</strong>;</item>
/// <item>uma data sai <c>25/01/2026</c>, e em invariante sai
/// <c>01/25/2026</c> — <strong>a mesma trilha passa a ter dois formatos</strong>
/// conforme o ambiente, e nenhum deles é ISO.</item>
/// </list>
///
/// <para>
/// Isto não é hipotético: verificou-se contra a base de dados que
/// <c>{"endedOn":"01/25/2026"}</c> era o que ficava gravado, e que um registo
/// mais antigo tinha <c>"08/11/2026"</c> — que um leitor angolano lê como 8 de
/// Novembro quando é 11 de Agosto.
/// </para>
///
/// <para>
/// <strong>A trilha é append-only (BR-14).</strong> Um registo mal formatado
/// não se corrige depois — só se evita antes.
/// </para>
/// </summary>
public class TrilhaIndependenteDaCulturaTests
{
    private static readonly DateOnly Inicio = new(2026, 1, 5);
    private static readonly DateOnly Fim = new(2026, 1, 25);

    private static readonly TimeProvider Relogio =
        new RelogioFixo(new DateTimeOffset(2026, 1, 25, 8, 0, 0, TimeSpan.Zero));

    private static AuditContext Actor() => new(Guid.NewGuid(), null, null);

    /// <summary>
    /// Corre o corpo com a cultura portuguesa activa — vírgula decimal e
    /// datas <c>dd/MM/yyyy</c>. É a cultura que a aplicação teria se o
    /// contentor definisse `LANG`, e a que uma máquina de desenvolvimento em
    /// Portugal ou Angola tem por omissão.
    /// </summary>
    private static async Task ComCulturaPortuguesa(Func<Task> corpo)
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-PT");

        try
        {
            await corpo();
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public async Task Data_De_Fecho_De_Manutencao_Sai_Em_Iso()
    {
        await ComCulturaPortuguesa(async () =>
        {
            var store = new FakeVehicleStore();
            var trilha = new FakeAuditTrail();
            var viatura = store.Registar("LD-01-AA");
            var registo = viatura.OpenMaintenance(MaintenanceType.Corrective, "Travões", Inicio);

            await new CloseMaintenance(store, trilha)
                .ExecuteAsync(viatura.Id, registo.Id, Fim, 1234.56m, Actor(), CancellationToken.None);

            var valor = Assert.Single(trilha.Registos).NewValue;

            // ISO, e não 25/01/2026 nem 01/25/2026.
            Assert.Contains("\"endedOn\":\"2026-01-25\"", valor);
            Assert.Contains("\"cost\":1234.56", valor);
        });
    }

    [Fact]
    public async Task Data_De_Abertura_De_Manutencao_Sai_Em_Iso()
    {
        await ComCulturaPortuguesa(async () =>
        {
            var store = new FakeVehicleStore();
            var trilha = new FakeAuditTrail();
            var viatura = store.Registar("LD-01-AA");

            await new OpenMaintenance(store, trilha)
                .ExecuteAsync(viatura.Id, MaintenanceType.Preventive, "Revisão", Inicio,
                    Actor(), CancellationToken.None);

            Assert.Contains("\"startedOn\":\"2026-01-05\"", Assert.Single(trilha.Registos).NewValue);
        });
    }

    [Fact]
    public async Task Datas_De_Atribuicao_De_Viatura_Saem_Em_Iso()
    {
        await ComCulturaPortuguesa(async () =>
        {
            var store = new FakeVehicleStore();
            var colaboradores = new FakeEmployeeDirectory();
            var trilha = new FakeAuditTrail();
            var viatura = store.Registar("LD-01-AA");

            await new AssignVehicle(store, colaboradores, trilha, Relogio)
                .ExecuteAsync(viatura.Id, colaboradores.Existente(), Inicio, Actor(), CancellationToken.None);

            Assert.Contains("\"startedOn\":\"2026-01-05\"", trilha.Registos[0].NewValue);
        });
    }

    /// <summary>
    /// A data do próximo vencimento é calculada pelo domínio e escrita pela
    /// trilha — e é a que diz quando a viatura tem de voltar à oficina.
    /// Ambígua, deixa de servir para isso.
    /// </summary>
    [Fact]
    public async Task Data_Do_Proximo_Vencimento_Sai_Em_Iso()
    {
        await ComCulturaPortuguesa(async () =>
        {
            var store = new FakeVehicleStore();
            var trilha = new FakeAuditTrail();
            var viatura = store.Registar("LD-01-AA");

            await new SchedulePlan(store, trilha)
                .ExecuteAsync(viatura.Id, "Revisão", 180, Fim, Actor(), CancellationToken.None);

            Assert.Contains("\"nextDueOn\":\"2026-01-25\"", Assert.Single(trilha.Registos).NewValue);
        });
    }
}
