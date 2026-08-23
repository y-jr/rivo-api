using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rivo.Hr.Application.UseCases;

namespace Rivo.Hr.Infrastructure.Reconciliation;

public sealed class PositionApprovalReconciliationOptions
{
    public const string SectionName = "PositionApprovalReconciliation";

    /// <summary>
    /// Intervalo entre varreduras.
    ///
    /// <para>
    /// Sessenta segundos e não dez, ao contrário da entrega de notificações: um
    /// minuto de atraso a tornar efectiva uma atribuição de cargo não incomoda
    /// ninguém, e cada ciclo custa uma consulta a duas bases de dados por
    /// atribuição pendente.
    /// </para>
    /// </summary>
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>Atribuições por ciclo. Limita a duração de cada varredura.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Desliga a varredura automática.
    ///
    /// <para>
    /// Existe para ambientes sem governança ligada, e para quem prefira aplicar
    /// as decisões pelo endpoint. Desligado, uma atribuição aprovada fica
    /// pendente até alguém chamar
    /// <c>POST /hr/position-assignments/{id}/approval-outcome</c> — o que é uma
    /// escolha legítima, mas tem de ser uma escolha.
    /// </para>
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Aplica, em ciclo, as decisões de governança aos processos de `hr` que
/// esperam por elas — atribuições de Cargo e pedidos de férias.
///
/// <para>
/// <strong>Existe porque `approval` não pode empurrar.</strong>
/// `modules/approval.md` proíbe-lhe modificar dados de negócio do módulo de
/// origem, e é `hr` que possui a atribuição. Alguém tem de perguntar; sem este
/// worker, esse alguém era uma pessoa a carregar num botão, e uma atribuição
/// aprovada ficava pendente até alguém se lembrar.
/// </para>
///
/// <para>
/// Sondagem, como o worker de entrega de `notifications` e pela mesma razão:
/// não há barramento de eventos, e a tabela já é a fila. Quando o mecanismo de
/// eventos de `approval` for decidido, isto passa a reagir em vez de sondar —
/// e o caso de uso que faz o trabalho não muda.
/// </para>
/// </summary>
public sealed class PositionApprovalReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PositionApprovalReconciliationOptions> options,
    ILogger<PositionApprovalReconciliationWorker> logger) : BackgroundService
{
    private readonly PositionApprovalReconciliationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Reconciliação de aprovações de `hr` desligada. As decisões têm de ser " +
                "aplicadas pelos endpoints `approval-outcome` de cada processo.");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        logger.LogInformation(
            "Reconciliação de aprovações de `hr` activa: intervalo {Interval}s, lote {BatchSize}.",
            _options.PollIntervalSeconds, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Scope por ciclo: os DbContext são scoped e não devem viver o
                // tempo todo do worker, que é singleton.
                using var scope = scopeFactory.CreateScope();

                // As duas filas de `hr` que esperam por decisão. Correm no mesmo
                // ciclo e no mesmo scope: sao a mesma pergunta a `approval`,
                // sobre agregados diferentes.
                var cargos = await scope.ServiceProvider
                    .GetRequiredService<ReconcilePendingAssignments>()
                    .ExecuteAsync(_options.BatchSize, stoppingToken);

                var ferias = await scope.ServiceProvider
                    .GetRequiredService<ReconcilePendingLeave>()
                    .ExecuteAsync(_options.BatchSize, stoppingToken);

                // Só se regista quando algo aconteceu. Um ciclo vazio a cada
                // minuto encheria os logs e escondia o que interessa.
                if (cargos.Applied > 0 || cargos.Failed > 0 || ferias.Applied > 0 || ferias.Failed > 0)
                {
                    logger.LogInformation(
                        "Reconciliação: cargos {CargosAplicados} aplicado(s)/{CargosFalhados} falhado(s); " +
                        "férias {FeriasAplicadas} aplicada(s)/{FeriasFalhadas} falhada(s).",
                        cargos.Applied, cargos.Failed, ferias.Applied, ferias.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // O worker não pode morrer por um ciclo mau — se morresse,
                // deixaria de haver reconciliação até ao próximo arranque, e
                // atribuições aprovadas ficariam pendentes em silêncio.
                logger.LogError(exception, "Ciclo de reconciliação de aprovações de `hr` falhou.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
