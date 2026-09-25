using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Application.UseCases;
using Rivo.Inventory.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Inventory.Application.Tests;

internal sealed class FakeInventoryCountStore : IInventoryCountStore
{
    private readonly List<InventoryCount> _contagens = [];

    public int Gravacoes { get; private set; }

    public InventoryCount Abrir(Guid warehouseId, DateOnly quando)
    {
        var contagem = InventoryCount.Open(warehouseId, quando);
        _contagens.Add(contagem);

        return contagem;
    }

    public Task<InventoryCount?> FindAsync(Guid countId, CancellationToken cancellationToken) =>
        Task.FromResult(_contagens.SingleOrDefault(c => c.Id == countId));

    public Task<InventoryCount?> FindForUpdateAsync(Guid countId, CancellationToken cancellationToken) =>
        Task.FromResult(_contagens.SingleOrDefault(c => c.Id == countId));

    public Task<(IReadOnlyList<InventoryCount> Items, int? TotalCount)> ListAsync(
        Guid? warehouseId, PageRequest? pagina, CancellationToken cancellationToken)
    {
        // Mantém o comportamento original do duplo: não filtra por
        // `warehouseId` (nenhum teste existente precisou disso) — só a
        // paginação é nova.
        var ordenados = _contagens.OrderByDescending(c => c.OccurredOn).ToList();

        return Task.FromResult(FakePaginacao.Paginar(ordenados, pagina));
    }

    public Task AddAsync(InventoryCount count, CancellationToken cancellationToken)
    {
        _contagens.Add(count);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Governança configurável: cada teste diz o que `approval` responderia, sem
/// precisar do motor real.
/// </summary>
internal sealed class FakeInventoryApproval : IInventoryApprovalSubmission
{
    private readonly InventoryApprovalSubmissionResult _resposta;

    public FakeInventoryApproval(InventoryApprovalSubmissionResult resposta, bool disponivel = true)
    {
        _resposta = resposta;
        IsAvailable = disponivel;
    }

    public bool IsAvailable { get; }

    public InventoryApprovalState Estado { get; set; } = InventoryApprovalState.Pending;

    /// <summary>O valor com que a submissão foi feita — é ele que escolhe a alçada.</summary>
    public decimal? ValorSubmetido { get; private set; }

    public string? ResumoSubmetido { get; private set; }

    public Guid? RequerenteSubmetido { get; private set; }

    public Task<InventoryApprovalSubmissionResult> SubmitAsync(
        Guid countId, Guid requestedByUserId, decimal varianceValue, string summary, CancellationToken cancellationToken)
    {
        ValorSubmetido = varianceValue;
        ResumoSubmetido = summary;
        RequerenteSubmetido = requestedByUserId;

        return Task.FromResult(_resposta);
    }

    public Task<InventoryApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(Estado);
}

/// <summary>
/// A governança das divergências de contagem (ADR-064).
///
/// <para>
/// O que estes testes protegem é uma propriedade só: <strong>quando a
/// divergência é submetida a decisão, o stock não muda</strong>. Tudo o resto —
/// o valor que escolhe a alçada, o motivo que fica no ajuste, o resumo na
/// trilha — existe para que alguém consiga decidir, ou perceber depois o que
/// foi decidido.
/// </para>
/// </summary>
public class ContagemComGovernancaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dia = new(2026, 9, 17);
    private static readonly Guid Armazem = Guid.CreateVersion7();
    private static readonly Guid Utilizador = Guid.CreateVersion7();

    private static AuditContext Contexto(Guid? actor = null) =>
        new(actor ?? Utilizador, "192.0.2.10", Guid.NewGuid().ToString());

    /// <summary>
    /// Um item com stock recebido e uma contagem que encontra menos do que o
    /// sistema esperava — a falta que a governança existe para vigiar.
    /// </summary>
    private static (FakeInventoryItemStore Itens, FakeInventoryCountStore Contagens, InventoryItem Item, InventoryCount Contagem)
        Cenario(decimal recebido, decimal custo, decimal contado)
    {
        var itens = new FakeInventoryItemStore();
        var contagens = new FakeInventoryCountStore();

        var item = itens.Registar("SKU-1");
        item.RegisterReceipt(Armazem, recebido, custo, "Recepcao inicial", Dia, Agora);

        var contagem = contagens.Abrir(Armazem, Dia);
        contagem.AddLine(item.Id, contado, item.QuantityOnHandAt(Armazem));

        return (itens, contagens, item, contagem);
    }

    private static CloseInventoryCount Fecho(
        FakeInventoryCountStore contagens,
        FakeInventoryItemStore itens,
        IInventoryApprovalSubmission governanca,
        FakeAuditTrail trilha) =>
        new(contagens, itens, governanca, trilha, new RelogioFixo(Agora));

    // ── O caminho que interessa: retido ────────────────────────────────────

    [Fact]
    public async Task ComAlcadaAplicavel_FicaPendenteEOStockNaoMuda()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var trilha = new FakeAuditTrail();
        var processo = Guid.CreateVersion7();
        var governanca = new FakeInventoryApproval(InventoryApprovalSubmissionResult.Submitted(processo));

        var resultado = await Fecho(contagens, itens, governanca, trilha)
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.PendingApproval, resultado.Outcome);
        Assert.Equal(processo, resultado.ApprovalRequestId);

        // **A propriedade central**: o armazém continua com o que tinha.
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));
        Assert.Equal(InventoryCountStatus.PendingApproval, contagem.Status);
        Assert.DoesNotContain(trilha.Registos, r => r.Action == "inventory.movement.adjustment");
        Assert.Contains(trilha.Registos, r => r.Action == "inventory.count.submitted");
        Assert.DoesNotContain(trilha.Registos, r => r.Action == "inventory.count.closed");
    }

    [Fact]
    public async Task OValorSubmetidoEAQuantidadeVezesOCustoMedio()
    {
        var (itens, contagens, _, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Submitted(Guid.CreateVersion7()));

        await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        // 40 unidades em falta × 250 de custo médio. É o valor que escolhe a
        // faixa da política — não a quantidade, que não distingue um artigo
        // barato de um caro.
        Assert.Equal(10_000m, governanca.ValorSubmetido);
        Assert.Equal(Utilizador, governanca.RequerenteSubmetido);
        Assert.Contains("2026-09-17", governanca.ResumoSubmetido);
    }

    // ── Os dois caminhos que aplicam ───────────────────────────────────────

    [Fact]
    public async Task SemDivergencia_FechaSemSequerPerguntarAGovernanca()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 100);
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Submitted(Guid.CreateVersion7()));

        var resultado = await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.Closed, resultado.Outcome);
        Assert.Empty(resultado.GeneratedAdjustmentIds!);
        Assert.Equal(InventoryCountStatus.Closed, contagem.Status);

        // Não há o que aprovar numa contagem que confirmou o sistema.
        Assert.Null(governanca.ValorSubmetido);
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));
    }

    [Fact]
    public async Task SemAlcadaQueCubraOValor_AplicaEDizNaTrilhaQueNaoHouveGovernanca()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 99);
        var trilha = new FakeAuditTrail();
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.NoApplicablePolicy());

        var resultado = await Fecho(contagens, itens, governanca, trilha)
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.Closed, resultado.Outcome);
        Assert.Equal(99, item.QuantityOnHandAt(Armazem));

        // Fica escrito que não houve alçada — é a diferença entre não ter sido
        // preciso e ter sido contornado.
        var fecho = Assert.Single(trilha.Registos, r => r.Action == "inventory.count.closed");
        Assert.Contains("\"approvalRequired\":false", fecho.NewValue);
        Assert.Contains("\"varianceValue\":250", fecho.NewValue);
    }

    [Fact]
    public async Task ComGovernancaBloqueada_NaoFechaNemCorrige()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Blocked("Nenhum aprovador resolvido para a política."));

        var resultado = await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        // Há alçada configurada e não foi cumprida: aplicar aqui era decidir, por
        // omissão, que a governança não conta.
        Assert.Equal(CloseCountOutcome.Conflict, resultado.Outcome);
        Assert.Contains("aprovador", resultado.Error);
        Assert.Equal(InventoryCountStatus.Open, contagem.Status);
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));
    }

    [Fact]
    public async Task SemMotorDeGovernanca_AplicaEmVezDeBloquear()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.NoApplicablePolicy(), disponivel: false);

        var resultado = await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        // Ao contrário da folha salarial, que fica em rascunho: uma contagem por
        // fechar deixa o stock do sistema a divergir do armazém real.
        Assert.Equal(CloseCountOutcome.Closed, resultado.Outcome);
        Assert.Equal(60, item.QuantityOnHandAt(Armazem));
    }

    // ── A decisão, depois ──────────────────────────────────────────────────

    [Fact]
    public async Task Aprovada_ADecisaoAplicaOsAjustesQueOFechoReteve()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var trilha = new FakeAuditTrail();
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Submitted(Guid.CreateVersion7()));
        var fecho = Fecho(contagens, itens, governanca, trilha);

        await fecho.ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));

        governanca.Estado = InventoryApprovalState.Approved;

        var resultado = await new ApplyInventoryCountDecision(
                contagens, governanca, fecho, trilha, new RelogioFixo(Agora))
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.Closed, resultado.Outcome);
        Assert.Single(resultado.GeneratedAdjustmentIds!);
        Assert.Equal(60, item.QuantityOnHandAt(Armazem));
        Assert.Equal(InventoryCountStatus.Closed, contagem.Status);
    }

    [Fact]
    public async Task Recusada_NaoCorrigeNadaENaoReabre()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var trilha = new FakeAuditTrail();
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Submitted(Guid.CreateVersion7()));
        var fecho = Fecho(contagens, itens, governanca, trilha);

        await fecho.ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);
        governanca.Estado = InventoryApprovalState.Refused;

        var resultado = await new ApplyInventoryCountDecision(
                contagens, governanca, fecho, trilha, new RelogioFixo(Agora))
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.Refused, resultado.Outcome);
        Assert.Equal(InventoryCountStatus.Refused, contagem.Status);
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));
        Assert.Contains(trilha.Registos, r => r.Action == "inventory.count.refused");

        // Recusada é facto histórico: não volta a aceitar linhas.
        Assert.Throws<InvalidOperationException>(() => contagem.AddLine(Guid.CreateVersion7(), 1, 0));
    }

    [Fact]
    public async Task PorDecidir_NaoCorrigeNadaEDizQueContinuaPendente()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var trilha = new FakeAuditTrail();
        var governanca = new FakeInventoryApproval(
            InventoryApprovalSubmissionResult.Submitted(Guid.CreateVersion7()));
        var fecho = Fecho(contagens, itens, governanca, trilha);

        await fecho.ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        var resultado = await new ApplyInventoryCountDecision(
                contagens, governanca, fecho, trilha, new RelogioFixo(Agora))
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        Assert.Equal(CloseCountOutcome.StillPending, resultado.Outcome);
        Assert.Equal(100, item.QuantityOnHandAt(Armazem));
    }

    // ── O motivo do ajuste, e o resumo na trilha ───────────────────────────

    [Fact]
    public async Task OMotivoDoAjusteExplicaADivergencia()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var governanca = new FakeInventoryApproval(InventoryApprovalSubmissionResult.NoApplicablePolicy());

        await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        var ajuste = item.Movements.Single(m => m.Type == StockMovementType.Adjustment);

        // Era «Contagem 01a0b2c3-…», que cumpria a regra de exigir motivo sem
        // explicar nada a quem o lia.
        Assert.Equal(
            "Contagem de 2026-09-17: esperado 100, contado 60 (falta 40)",
            ajuste.Reason);
    }

    [Fact]
    public async Task OMotivoDistingueSobraDeFalta()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 103);
        var governanca = new FakeInventoryApproval(InventoryApprovalSubmissionResult.NoApplicablePolicy());

        await Fecho(contagens, itens, governanca, new FakeAuditTrail())
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        var ajuste = item.Movements.Single(m => m.Type == StockMovementType.Adjustment);
        Assert.Contains("sobra 3", ajuste.Reason);
    }

    [Fact]
    public async Task OFechoResumeADivergenciaEOAjusteLevaOEsperadoEOContado()
    {
        var (itens, contagens, item, contagem) = Cenario(recebido: 100, custo: 250, contado: 60);
        var trilha = new FakeAuditTrail();
        var governanca = new FakeInventoryApproval(InventoryApprovalSubmissionResult.NoApplicablePolicy());

        await Fecho(contagens, itens, governanca, trilha)
            .ExecuteAsync(contagem.Id, Contexto(), CancellationToken.None);

        var fecho = Assert.Single(trilha.Registos, r => r.Action == "inventory.count.closed");
        Assert.Contains("\"linesCounted\":1", fecho.NewValue);
        Assert.Contains("\"linesWithVariance\":1", fecho.NewValue);
        Assert.Contains("\"shortfall\":40", fecho.NewValue);
        Assert.Contains("\"surplus\":0", fecho.NewValue);
        Assert.Contains("\"occurredOn\":\"2026-09-17\"", fecho.NewValue);

        var ajuste = Assert.Single(trilha.Registos, r => r.Action == "inventory.movement.adjustment");
        Assert.Contains("\"expectedQuantity\":100", ajuste.NewValue);
        Assert.Contains("\"countedQuantity\":60", ajuste.NewValue);
        Assert.Contains("\"quantity\":-40", ajuste.NewValue);
        Assert.Contains($"\"countId\":\"{contagem.Id}\"", ajuste.NewValue);
        Assert.Contains($"\"itemId\":\"{item.Id}\"", ajuste.NewValue);
    }
}
