using Rivo.Audit.Contracts;
using Rivo.Inventory.Application.Abstractions;
using Rivo.Inventory.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Vista de leitura de uma contagem, com as suas linhas. Mesma disciplina de
/// <c>InventoryItemView</c> — a entidade de domínio nunca sai desta camada.
/// </summary>
public sealed record InventoryCountView(
    Guid CountId,
    Guid WarehouseId,
    DateOnly OccurredOn,
    string Status,
    string? CancellationReason,
    IReadOnlyList<InventoryCountLineView> Lines);

public sealed record InventoryCountLineView(
    Guid LineId, Guid ItemId, decimal ExpectedQuantity, decimal CountedQuantity, decimal Variance);

internal static class InventoryCountViews
{
    internal static InventoryCountView ToView(InventoryCount count) => new(
        count.Id,
        count.WarehouseId,
        count.OccurredOn,
        count.Status.ToString(),
        count.CancellationReason,
        [.. count.Lines.Select(l => new InventoryCountLineView(l.Id, l.ItemId, l.ExpectedQuantity, l.CountedQuantity, l.Variance))]);
}

public sealed class ListInventoryCounts(IInventoryCountStore store)
{
    public async Task<(IReadOnlyList<InventoryCountView> Items, int? TotalCount)> ExecuteAsync(
        Guid? warehouseId, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var (contagens, total) = await store.ListAsync(warehouseId, pagina, cancellationToken);
        return ([.. contagens.Select(InventoryCountViews.ToView)], total);
    }
}

public sealed class GetInventoryCount(IInventoryCountStore store)
{
    public async Task<InventoryCountView?> ExecuteAsync(Guid countId, CancellationToken cancellationToken)
    {
        var contagem = await store.FindAsync(countId, cancellationToken);
        return contagem is null ? null : InventoryCountViews.ToView(contagem);
    }
}

public sealed class OpenInventoryCount(IInventoryCountStore store, IWarehouseStore warehouses, IAuditTrail audit)
{
    public async Task<OpenCountResult> ExecuteAsync(
        Guid warehouseId, DateOnly occurredOn, AuditContext context, CancellationToken cancellationToken)
    {
        switch (await WarehouseGuard.CheckAsync(warehouses, warehouseId, cancellationToken))
        {
            case WarehouseUsability.NotFound:
                return OpenCountResult.NotFound("Armazém não encontrado.");
            case WarehouseUsability.Inactive:
                return OpenCountResult.Conflict("Armazém inactivo.");
        }

        InventoryCount count;

        try
        {
            count = InventoryCount.Open(warehouseId, occurredOn);
        }
        catch (ArgumentException error)
        {
            return OpenCountResult.Rejected(error.Message);
        }

        await store.AddAsync(count, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountOpened,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"warehouseId":"{{warehouseId}}","occurredOn":"{{occurredOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return OpenCountResult.Success(count.Id);
    }
}

public sealed class AddInventoryCountLine(IInventoryCountStore counts, IInventoryItemStore items, IAuditTrail audit)
{
    public async Task<AddCountLineResult> ExecuteAsync(
        Guid countId, Guid itemId, decimal countedQuantity, AuditContext context, CancellationToken cancellationToken)
    {
        var count = await counts.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return AddCountLineResult.NotFound("Contagem não encontrada.");
        }

        var item = await items.FindAsync(itemId, cancellationToken);

        if (item is null)
        {
            return AddCountLineResult.NotFound("Item não encontrado.");
        }

        var expectedQuantity = item.QuantityOnHandAt(count.WarehouseId);

        InventoryCountLine linha;

        try
        {
            linha = count.AddLine(itemId, countedQuantity, expectedQuantity);
        }
        catch (ArgumentException error)
        {
            return AddCountLineResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return AddCountLineResult.Conflict(error.Message);
        }

        await counts.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountLineAdded,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"itemId":"{{itemId}}","expectedQuantity":{{linha.ExpectedQuantity}},"countedQuantity":{{linha.CountedQuantity}}}"""),
            cancellationToken);

        return AddCountLineResult.Success(linha.Id, linha.ExpectedQuantity, linha.CountedQuantity, linha.Variance);
    }
}

/// <summary>
/// Fecha a contagem — e, desde o ADR-064, é aqui que se decide se as
/// divergências se aplicam já ou se passam por governança.
///
/// <para>
/// <strong>Três caminhos, e o meio é o que é novo:</strong>
/// </para>
///
/// <list type="number">
///   <item>
///     <strong>Sem divergências</strong> — fecha e não gera nada. Não há o que
///     aprovar numa contagem que confirmou o sistema.
///   </item>
///   <item>
///     <strong>Com divergências, e há alçada que as cubra</strong> — fica
///     <see cref="InventoryCountStatus.PendingApproval"/> e <strong>o stock não
///     muda</strong>. Uma falta de inventário acima da alçada é uma perda a
///     explicar, não um número a arrumar em silêncio.
///   </item>
///   <item>
///     <strong>Com divergências, e nenhuma alçada as cobre</strong> — fecha e
///     aplica, como antes. Quem decide o que precisa de aprovação é quem
///     configura as políticas; este módulo pergunta e obedece.
///   </item>
/// </list>
///
/// <para>
/// Quando aplica, gera um Ajuste por cada linha com variância <strong>na mesma
/// transacção</strong> — tudo ou nada: se um item recusar o ajuste (por
/// exemplo, ficou inactivo entretanto), nada fica gravado, nem sequer o fecho.
/// Mesma disciplina de "Emitir passa a lançar, na mesma transacção" de
/// `finance`.
/// </para>
/// </summary>
public sealed class CloseInventoryCount(
    IInventoryCountStore counts,
    IInventoryItemStore items,
    IInventoryApprovalSubmission approvals,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<CloseCountResult> ExecuteAsync(
        Guid countId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var count = await counts.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return CloseCountResult.NotFound("Contagem não encontrada.");
        }

        if (count.Status is not InventoryCountStatus.Open)
        {
            return CloseCountResult.Conflict($"Não é possível fechar: a contagem já está {count.Status}.");
        }

        if (count.Lines.Count == 0)
        {
            return CloseCountResult.Conflict("Uma contagem sem nenhuma linha não tem o que confirmar.");
        }

        var divergentes = count.LinesWithVariance;

        // Sem divergências não há o que aprovar nem o que corrigir.
        if (divergentes.Count == 0)
        {
            return await AplicarAsync(count, context, cancellationToken);
        }

        var valor = await ValorDaDivergenciaAsync(divergentes, cancellationToken);

        // O actor do contexto é a conta autenticada — a mesma que a trilha
        // regista. Sem ela não há quem requeira a decisão, e o caminho é o de
        // sempre: aplicar, com a ausência de governança escrita na trilha.
        if (!approvals.IsAvailable || context.ActorId is not { } requerente)
        {
            // Sem motor de governança, ou sem colaborador que possa requerer,
            // aplica-se — e **fica escrito na trilha que não houve alçada**, que
            // é a diferença entre não ter sido preciso e ter sido contornado.
            return await AplicarAsync(count, context, cancellationToken, valorSemGovernanca: valor);
        }

        var submissao = await approvals.SubmitAsync(
            count.Id,
            requerente,
            valor,
            $"Contagem de {count.OccurredOn:yyyy-MM-dd}: {divergentes.Count} divergência(s)",
            cancellationToken);

        switch (submissao.Outcome)
        {
            case InventoryApprovalOutcome.Submitted:
                count.MarkSubmitted(submissao.RequestId!.Value, valor, clock.GetUtcNow());
                await counts.SaveChangesAsync(cancellationToken);

                await audit.RecordAsync(
                    new AuditRecord(
                        InventoryAuditActions.CountSubmitted,
                        InventoryAuditEntityTypes.Count,
                        count.Id.ToString(),
                        context,
                        NewValue: $$"""{"approvalRequest":"{{count.ApprovalRequestId}}","varianceValue":{{valor}},"linesWithVariance":{{divergentes.Count}}}"""),
                    cancellationToken);

                return CloseCountResult.PendingApproval(count.ApprovalRequestId!.Value, valor);

            case InventoryApprovalOutcome.Blocked:
                // Há governança configurada e não foi cumprida. Aplicar aqui era
                // decidir, por omissão, que a alçada não conta.
                return CloseCountResult.Conflict(submissao.Reason!);

            default:
                return await AplicarAsync(count, context, cancellationToken, valorSemGovernanca: valor);
        }
    }

    /// <summary>
    /// Fecha e gera os ajustes. Chamado quando não houve divergências, quando
    /// nenhuma alçada as cobria, e — por <see cref="ApplyInventoryCountDecision"/> —
    /// quando foram aprovadas.
    /// </summary>
    internal async Task<CloseCountResult> AplicarAsync(
        InventoryCount count,
        AuditContext context,
        CancellationToken cancellationToken,
        decimal? valorSemGovernanca = null)
    {
        try
        {
            count.Close();
        }
        catch (InvalidOperationException error)
        {
            return CloseCountResult.Conflict(error.Message);
        }

        var agora = clock.GetUtcNow();
        var geradas = new List<AjusteGerado>();

        foreach (var linha in count.LinesWithVariance)
        {
            var item = await items.FindForUpdateAsync(linha.ItemId, cancellationToken);

            if (item is null)
            {
                return CloseCountResult.Conflict($"Item {linha.ItemId} da contagem já não existe.");
            }

            StockMovement movimento;

            try
            {
                movimento = item.RegisterAdjustment(
                    count.WarehouseId, linha.Variance, MotivoDoAjuste(count, linha), count.OccurredOn, agora);
            }
            catch (InvalidOperationException error)
            {
                return CloseCountResult.Conflict($"Item {linha.ItemId}: {error.Message}");
            }

            geradas.Add(new AjusteGerado(movimento.Id, linha.ItemId, linha.ExpectedQuantity, linha.CountedQuantity, linha.Variance));
        }

        count.MarkSettled(agora);
        await counts.SaveChangesAsync(cancellationToken);

        var faltas = geradas.Where(g => g.Variance < 0).Sum(g => -g.Variance);
        var sobras = geradas.Where(g => g.Variance > 0).Sum(g => g.Variance);

        // **O resumo da divergência vai no próprio evento de fecho.** Antes ia
        // só o número de linhas divergentes, e quem auditava tinha de ir juntar
        // os eventos de linha com os dos ajustes à mão para saber o que tinha
        // acontecido. O detalhe de cada linha continua no evento do seu ajuste —
        // aqui fica o que se lê num relance: quanto faltou, quanto sobrou, e
        // quanto vale.
        var governanca = valorSemGovernanca is { } v
            ? $$""","varianceValue":{{v}},"approvalRequired":false"""
            : count.ApprovalRequestId is { } pedido
                ? $$""","approvalRequest":"{{pedido}}","approvalRequired":true"""
                : string.Empty;

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountClosed,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"warehouseId":"{{count.WarehouseId}}","occurredOn":"{{count.OccurredOn:yyyy-MM-dd}}","linesCounted":{{count.Lines.Count}},"linesWithVariance":{{geradas.Count}},"shortfall":{{faltas}},"surplus":{{sobras}}{{governanca}}}"""),
            cancellationToken);

        foreach (var ajuste in geradas)
        {
            // O ajuste passa a levar o esperado e o contado, e não só a
            // diferença: quem lê a trilha do movimento vê de onde veio o número
            // sem ter de ir buscar a linha da contagem.
            await audit.RecordAsync(
                new AuditRecord(
                    InventoryAuditActions.MovementAdjustment,
                    InventoryAuditEntityTypes.Movement,
                    ajuste.MovementId.ToString(),
                    context,
                    NewValue: $$"""{"itemId":"{{ajuste.ItemId}}","warehouseId":"{{count.WarehouseId}}","expectedQuantity":{{ajuste.Expected}},"countedQuantity":{{ajuste.Counted}},"quantity":{{ajuste.Variance}},"countId":"{{count.Id}}"}"""),
                cancellationToken);
        }

        return CloseCountResult.Success([.. geradas.Select(g => g.MovementId)]);
    }

    /// <summary>
    /// O motivo que fica gravado no Ajuste, e que aparece na lista de
    /// movimentos.
    ///
    /// <para>
    /// Era o identificador da contagem — <c>"Contagem 01a0b2c3-…"</c> —, que
    /// cumpria a regra de «um Ajuste exige motivo» sem explicar nada a quem o
    /// lia. Passa a dizer o que aconteceu: a data da contagem física, o que o
    /// sistema esperava, o que se encontrou, e se foi falta ou sobra.
    /// </para>
    /// </summary>
    private static string MotivoDoAjuste(InventoryCount count, InventoryCountLine linha)
    {
        var sentido = linha.Variance < 0 ? "falta" : "sobra";

        return $"Contagem de {count.OccurredOn:yyyy-MM-dd}: esperado {linha.ExpectedQuantity:0.####}, "
             + $"contado {linha.CountedQuantity:0.####} ({sentido} {Math.Abs(linha.Variance):0.####})";
    }

    /// <summary>
    /// Quanto vale a divergência: soma de |variância| × custo médio do item.
    ///
    /// <para>
    /// É este número que escolhe a faixa da política — uma falta de mil
    /// unidades de um artigo barato não vale o mesmo que uma de dez de um
    /// artigo caro, e é o valor, não a quantidade, que decide se alguém tem de
    /// olhar para isto.
    /// </para>
    /// </summary>
    private async Task<decimal> ValorDaDivergenciaAsync(
        IReadOnlyList<InventoryCountLine> divergentes,
        CancellationToken cancellationToken)
    {
        var total = 0m;

        foreach (var linha in divergentes)
        {
            var item = await items.FindAsync(linha.ItemId, cancellationToken);
            total += Math.Abs(linha.Variance) * (item?.AverageCost ?? 0m);
        }

        return total;
    }

    private sealed record AjusteGerado(
        Guid MovementId, Guid ItemId, decimal Expected, decimal Counted, decimal Variance);
}

/// <summary>
/// Pergunta a `approval` se a contagem já foi decidida e aplica o efeito deste
/// lado (ADR-064).
///
/// <para>
/// <strong>`approval` nunca empurra.</strong> Mesma disciplina de
/// <c>ApplyPayrollDecision</c>: o módulo dono pergunta quando quer saber, e é
/// ele que produz o efeito que reteve — aqui, gerar os ajustes que o fecho
/// deixou por gerar.
/// </para>
/// </summary>
public sealed class ApplyInventoryCountDecision(
    IInventoryCountStore counts,
    IInventoryApprovalSubmission approvals,
    CloseInventoryCount fecho,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<CloseCountResult> ExecuteAsync(
        Guid countId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var count = await counts.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return CloseCountResult.NotFound("Contagem não encontrada.");
        }

        if (count.Status is not InventoryCountStatus.PendingApproval)
        {
            // Já decidida, ou nunca submetida: não é erro, é a resposta.
            return CloseCountResult.AlreadySettled(count.Status.ToString());
        }

        var estado = await approvals.GetStateAsync(count.ApprovalRequestId!.Value, cancellationToken);

        switch (estado)
        {
            case InventoryApprovalState.Approved:
                return await fecho.AplicarAsync(count, context, cancellationToken);

            case InventoryApprovalState.Refused:
                count.MarkRefused(clock.GetUtcNow());
                await counts.SaveChangesAsync(cancellationToken);

                await audit.RecordAsync(
                    new AuditRecord(
                        InventoryAuditActions.CountRefused,
                        InventoryAuditEntityTypes.Count,
                        count.Id.ToString(),
                        context,
                        NewValue: $$"""{"approvalRequest":"{{count.ApprovalRequestId}}","varianceValue":{{count.SubmittedVarianceValue}}}"""),
                    cancellationToken);

                return CloseCountResult.Refused();

            default:
                return CloseCountResult.StillPending();
        }
    }
}

public sealed class CancelInventoryCount(IInventoryCountStore store, IAuditTrail audit)
{
    public async Task<CancelCountResult> ExecuteAsync(
        Guid countId, string reason, AuditContext context, CancellationToken cancellationToken)
    {
        var count = await store.FindForUpdateAsync(countId, cancellationToken);

        if (count is null)
        {
            return CancelCountResult.NotFound("Contagem não encontrada.");
        }

        try
        {
            count.Cancel(reason);
        }
        catch (ArgumentException error)
        {
            return CancelCountResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return CancelCountResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                InventoryAuditActions.CountCancelled,
                InventoryAuditEntityTypes.Count,
                count.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{count.CancellationReason}}"}"""),
            cancellationToken);

        return CancelCountResult.Success();
    }
}

public sealed record OpenCountResult(OpenCountOutcome Outcome, Guid? CountId, string? Error)
{
    public static OpenCountResult Success(Guid countId) => new(OpenCountOutcome.Opened, countId, null);

    public static OpenCountResult NotFound(string error) => new(OpenCountOutcome.NotFound, null, error);

    public static OpenCountResult Rejected(string error) => new(OpenCountOutcome.Rejected, null, error);

    public static OpenCountResult Conflict(string error) => new(OpenCountOutcome.Conflict, null, error);
}

public enum OpenCountOutcome
{
    Opened,
    NotFound,

    /// <summary>Pedido malformado — sem armazém. 400.</summary>
    Rejected,

    /// <summary>Armazém inactivo. 409.</summary>
    Conflict,
}

public sealed record AddCountLineResult(
    AddCountLineOutcome Outcome, Guid? LineId, decimal? ExpectedQuantity, decimal? CountedQuantity, decimal? Variance, string? Error)
{
    public static AddCountLineResult Success(Guid lineId, decimal expectedQuantity, decimal countedQuantity, decimal variance) =>
        new(AddCountLineOutcome.Added, lineId, expectedQuantity, countedQuantity, variance, null);

    public static AddCountLineResult NotFound(string error) => new(AddCountLineOutcome.NotFound, null, null, null, null, error);

    public static AddCountLineResult Rejected(string error) => new(AddCountLineOutcome.Rejected, null, null, null, null, error);

    public static AddCountLineResult Conflict(string error) => new(AddCountLineOutcome.Conflict, null, null, null, null, error);
}

public enum AddCountLineOutcome
{
    Added,
    NotFound,

    /// <summary>Pedido malformado — sem item, ou quantidade negativa. 400.</summary>
    Rejected,

    /// <summary>Contagem já não está aberta, ou item já tem linha nesta contagem. 409.</summary>
    Conflict,
}

public sealed record CloseCountResult(
    CloseCountOutcome Outcome,
    IReadOnlyList<Guid>? GeneratedAdjustmentIds,
    string? Error,
    Guid? ApprovalRequestId = null,
    decimal? VarianceValue = null,
    string? SettledStatus = null)
{
    public static CloseCountResult Success(IReadOnlyList<Guid> generatedAdjustmentIds) =>
        new(CloseCountOutcome.Closed, generatedAdjustmentIds, null);

    public static CloseCountResult NotFound(string error) => new(CloseCountOutcome.NotFound, null, error);

    public static CloseCountResult Conflict(string error) => new(CloseCountOutcome.Conflict, null, error);

    /// <summary>
    /// Submetida a decisão. <strong>Nenhum ajuste foi gerado</strong> — é o que
    /// distingue este desfecho de <see cref="Success"/>, e é a razão de não
    /// devolver lista nenhuma de movimentos.
    /// </summary>
    public static CloseCountResult PendingApproval(Guid approvalRequestId, decimal varianceValue) =>
        new(CloseCountOutcome.PendingApproval, null, null, approvalRequestId, varianceValue);

    public static CloseCountResult StillPending() => new(CloseCountOutcome.StillPending, null, null);

    public static CloseCountResult Refused() => new(CloseCountOutcome.Refused, null, null);

    public static CloseCountResult AlreadySettled(string status) =>
        new(CloseCountOutcome.AlreadySettled, null, null, SettledStatus: status);
}

public enum CloseCountOutcome
{
    Closed,
    NotFound,

    /// <summary>Contagem já não está aberta, sem nenhuma linha, ou um item recusou o ajuste gerado. 409.</summary>
    Conflict,

    /// <summary>
    /// Submetida a decisão (ADR-064): o stock **não** mudou, e não há ajustes
    /// para devolver. 202.
    /// </summary>
    PendingApproval,

    /// <summary>Perguntou-se e ninguém decidiu ainda. Não é erro — é a resposta.</summary>
    StillPending,

    /// <summary>A divergência foi recusada. A contagem não se aplica e não reabre.</summary>
    Refused,

    /// <summary>Já decidida antes, ou nunca submetida. Devolve o estado em que está.</summary>
    AlreadySettled,
}

public sealed record CancelCountResult(CancelCountOutcome Outcome, string? Error)
{
    public static CancelCountResult Success() => new(CancelCountOutcome.Cancelled, null);

    public static CancelCountResult NotFound(string error) => new(CancelCountOutcome.NotFound, error);

    public static CancelCountResult Rejected(string error) => new(CancelCountOutcome.Rejected, error);

    public static CancelCountResult Conflict(string error) => new(CancelCountOutcome.Conflict, error);
}

public enum CancelCountOutcome
{
    Cancelled,
    NotFound,

    /// <summary>Pedido malformado — sem motivo. 400.</summary>
    Rejected,

    /// <summary>Contagem já não está aberta. 409.</summary>
    Conflict,
}
