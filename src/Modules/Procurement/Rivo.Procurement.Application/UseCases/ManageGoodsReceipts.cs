using Rivo.Audit.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.UseCases;

public sealed record GoodsReceiptView(
    Guid GoodsReceiptId,
    Guid PurchaseOrderId,
    DateOnly ReceivedOn,
    Guid ReceivedByEmployeeId,
    string? DeliveryNote,
    string Status,
    string? CancellationReason,
    IReadOnlyList<GoodsReceiptLineView> Lines);

public sealed record GoodsReceiptLineView(
    Guid LineId,
    Guid PurchaseOrderLineId,
    decimal QuantityReceived);

public sealed record NewGoodsReceiptLine(Guid PurchaseOrderLineId, decimal QuantityReceived);

internal static class GoodsReceiptViews
{
    internal static GoodsReceiptView ToView(GoodsReceipt recepcao) =>
        new(
            recepcao.Id,
            recepcao.PurchaseOrderId,
            recepcao.ReceivedOn,
            recepcao.ReceivedByEmployeeId,
            recepcao.DeliveryNote,
            recepcao.Status.ToString(),
            recepcao.CancellationReason,
            [.. recepcao.Lines.Select(l => new GoodsReceiptLineView(
                l.Id, l.PurchaseOrderLineId, l.QuantityReceived))]);
}

/// <summary>
/// Regista o que chegou contra uma ordem de compra.
///
/// <para>
/// <strong>É aqui que as três regras vivem</strong>, e nenhuma delas cabe no
/// agregado: a ordem tem de estar em vigor, cada contagem tem de ser de uma
/// linha dessa ordem, e o acumulado recebido não pode passar o encomendado. As
/// duas últimas são invariantes sobre o conjunto de recepções — o agregado vê
/// uma, e a regra é sobre todas.
/// </para>
/// </summary>
public sealed class RegisterGoodsReceipt(
    IProcurementStore store,
    IEmployeeDirectory employees,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<RegisterGoodsReceiptResult> ExecuteAsync(
        Guid purchaseOrderId,
        Guid receivedByEmployeeId,
        DateOnly? receivedOn,
        string? deliveryNote,
        IReadOnlyList<NewGoodsReceiptLine> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var agora = clock.GetUtcNow();

        var ordem = await store.FindOrderAsync(purchaseOrderId, cancellationToken);

        if (ordem is null)
        {
            return RegisterGoodsReceiptResult.OrderNotFound();
        }

        // Uma ordem cancelada foi retirada ao fornecedor. Se ainda chegou
        // mercadoria contra ela, o que há para resolver é a ordem — não é
        // registar a entrada e fingir que estava tudo bem.
        if (ordem.Status is not PurchaseOrderStatus.Issued)
        {
            return RegisterGoodsReceiptResult.OrderNotOpen(
                $"A ordem está em {ordem.Status} e não recebe mercadoria.");
        }

        // Quem recebeu tem de existir, e existe em `hr` (ADR-010).
        var colaborador = await employees.FindAsync(receivedByEmployeeId, agora, cancellationToken);

        if (colaborador is null)
        {
            return RegisterGoodsReceiptResult.ReceiverNotFound();
        }

        if (lines.Count == 0)
        {
            return RegisterGoodsReceiptResult.Rejected(
                "Uma recepção sem linhas não regista chegada nenhuma.");
        }

        GoodsReceipt recepcao;

        try
        {
            recepcao = GoodsReceipt.Register(
                ordem.Id,
                receivedOn ?? DateOnly.FromDateTime(agora.UtcDateTime),
                receivedByEmployeeId,
                deliveryNote);

            foreach (var linha in lines)
            {
                recepcao.AddLine(linha.PurchaseOrderLineId, linha.QuantityReceived);
            }
        }
        catch (Exception error)
            when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return RegisterGoodsReceiptResult.Rejected(error.Message);
        }

        var recebidoAntes = await store.ReceivedByOrderLineAsync(ordem.Id, cancellationToken);

        foreach (var contagem in recepcao.Lines)
        {
            var encomendada = ordem.Lines.FirstOrDefault(l => l.Id == contagem.PurchaseOrderLineId);

            // Contar uma linha que não é desta ordem é engano de quem regista, e
            // deixá-lo passar poria a recepção a satisfazer uma encomenda
            // diferente da que se pretende.
            if (encomendada is null)
            {
                return RegisterGoodsReceiptResult.LineNotInOrder(
                    $"A linha {contagem.PurchaseOrderLineId} não pertence a esta ordem de compra.");
            }

            var jaRecebido = recebidoAntes.TryGetValue(contagem.PurchaseOrderLineId, out var quanto)
                ? quanto
                : 0m;

            var porReceber = encomendada.Quantity - jaRecebido;

            if (contagem.QuantityReceived > porReceber)
            {
                // **Recusa, e não tolerância.** Receber a mais acontece — o
                // fornecedor engana-se, ou manda de propósito —, e um limiar de
                // excesso aceitável é decisão de negócio que não está em fonte
                // nenhuma deste repositório. Aceitar em silêncio faria a
                // empresa dever mais do que encomendou, e o 3-way match deixava
                // de ter contra que comparar.
                return RegisterGoodsReceiptResult.ExceedsOrdered(
                    $"A linha \"{encomendada.Description}\" foi encomendada em {encomendada.Quantity:0.####} " +
                    $"e já tem {jaRecebido:0.####} recebidas. Faltam {porReceber:0.####}, e esta recepção " +
                    $"traz {contagem.QuantityReceived:0.####}. Receber acima do encomendado precisa de " +
                    "outra ordem, não de uma contagem maior.");
            }
        }

        await store.AddReceiptAsync(recepcao, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.ReceiptRegistered,
                ProcurementAuditEntityTypes.GoodsReceipt,
                recepcao.Id.ToString(),
                context,
                NewValue: $$"""{"order":"{{recepcao.PurchaseOrderId}}","receivedBy":"{{receivedByEmployeeId}}","lines":{{recepcao.Lines.Count}},"deliveryNote":"{{recepcao.DeliveryNote}}"}"""),
            cancellationToken);

        return RegisterGoodsReceiptResult.Success(recepcao.Id);
    }
}

public sealed record RegisterGoodsReceiptResult(
    RegisterGoodsReceiptOutcome Outcome,
    Guid? GoodsReceiptId,
    string? Error)
{
    public static RegisterGoodsReceiptResult Success(Guid goodsReceiptId) =>
        new(RegisterGoodsReceiptOutcome.Registered, goodsReceiptId, null);

    public static RegisterGoodsReceiptResult OrderNotFound() =>
        new(RegisterGoodsReceiptOutcome.OrderNotFound, null, "Ordem de compra não encontrada.");

    public static RegisterGoodsReceiptResult ReceiverNotFound() =>
        new(RegisterGoodsReceiptOutcome.ReceiverNotFound, null, "Colaborador que recebeu não encontrado.");

    public static RegisterGoodsReceiptResult OrderNotOpen(string error) =>
        new(RegisterGoodsReceiptOutcome.OrderNotOpen, null, error);

    public static RegisterGoodsReceiptResult LineNotInOrder(string error) =>
        new(RegisterGoodsReceiptOutcome.LineNotInOrder, null, error);

    public static RegisterGoodsReceiptResult ExceedsOrdered(string error) =>
        new(RegisterGoodsReceiptOutcome.ExceedsOrdered, null, error);

    public static RegisterGoodsReceiptResult Rejected(string error) =>
        new(RegisterGoodsReceiptOutcome.Rejected, null, error);
}

public enum RegisterGoodsReceiptOutcome
{
    Registered,
    OrderNotFound,
    ReceiverNotFound,

    /// <summary>A ordem está cancelada. 409.</summary>
    OrderNotOpen,

    /// <summary>A contagem é de uma linha de outra ordem. 409.</summary>
    LineNotInOrder,

    /// <summary>Chegou mais do que foi encomendado. 409.</summary>
    ExceedsOrdered,

    Rejected,
}

public sealed class ListGoodsReceipts(IProcurementStore store)
{
    public async Task<IReadOnlyList<GoodsReceiptView>> ExecuteAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var recepcoes = await store.ListReceiptsAsync(purchaseOrderId, cancellationToken);

        return [.. recepcoes.Select(GoodsReceiptViews.ToView)];
    }
}

public sealed class GetGoodsReceipt(IProcurementStore store)
{
    public async Task<GoodsReceiptView?> ExecuteAsync(
        Guid goodsReceiptId,
        CancellationToken cancellationToken)
    {
        var recepcao = await store.FindReceiptAsync(goodsReceiptId, cancellationToken);

        return recepcao is null ? null : GoodsReceiptViews.ToView(recepcao);
    }
}

/// <summary>
/// Anula uma recepção registada por engano. Nunca elimina — BR-14.
/// </summary>
public sealed class CancelGoodsReceipt(IProcurementStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelGoodsReceiptResult> ExecuteAsync(
        Guid goodsReceiptId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var recepcao = await store.FindReceiptForUpdateAsync(goodsReceiptId, cancellationToken);

        if (recepcao is null)
        {
            return CancelGoodsReceiptResult.NotFound();
        }

        try
        {
            recepcao.Cancel(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelGoodsReceiptResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.ReceiptCancelled,
                ProcurementAuditEntityTypes.GoodsReceipt,
                recepcao.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{recepcao.CancellationReason}}"}"""),
            cancellationToken);

        return CancelGoodsReceiptResult.Success();
    }
}

public sealed record CancelGoodsReceiptResult(CancelGoodsReceiptOutcome Outcome, string? Error)
{
    public static CancelGoodsReceiptResult Success() => new(CancelGoodsReceiptOutcome.Cancelled, null);

    public static CancelGoodsReceiptResult NotFound() =>
        new(CancelGoodsReceiptOutcome.NotFound, "Recepção não encontrada.");

    public static CancelGoodsReceiptResult Rejected(string error) =>
        new(CancelGoodsReceiptOutcome.Rejected, error);
}

public enum CancelGoodsReceiptOutcome
{
    Cancelled,
    NotFound,
    Rejected,
}
