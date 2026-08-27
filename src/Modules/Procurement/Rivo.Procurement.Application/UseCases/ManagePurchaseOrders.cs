using Rivo.Audit.Contracts;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Application.UseCases;

public sealed record PurchaseOrderView(
    Guid PurchaseOrderId,
    Guid RequisitionId,
    Guid SupplierId,
    string SupplierName,
    string Currency,
    decimal Total,
    DateOnly IssuedOn,
    DateOnly? ExpectedOn,
    string Status,
    string? CancellationReason,
    IReadOnlyList<PurchaseOrderLineView> Lines);

public sealed record PurchaseOrderLineView(
    Guid LineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <param name="UnitPrice">
/// Preço <strong>acordado</strong> com o fornecedor. Não é o estimado na
/// requisição — entre os dois houve cotação, e é essa a razão de este passo
/// existir.
/// </param>
public sealed record NewPurchaseOrderLine(string Description, decimal Quantity, decimal UnitPrice);

internal static class PurchaseOrderViews
{
    internal static PurchaseOrderView ToView(PurchaseOrder ordem, string supplierName) =>
        new(
            ordem.Id,
            ordem.RequisitionId,
            ordem.SupplierId,
            supplierName,
            ordem.Currency,
            ordem.Total,
            ordem.IssuedOn,
            ordem.ExpectedOn,
            ordem.Status.ToString(),
            ordem.CancellationReason,
            [.. ordem.Lines.Select(l => new PurchaseOrderLineView(
                l.Id, l.Description, l.Quantity, l.UnitPrice, l.LineTotal))]);
}

/// <summary>
/// Emite uma ordem de compra a partir de uma requisição aprovada.
///
/// <para>
/// <strong>É aqui que a regra de `modules/procurement.md` é imposta</strong> —
/// "Ordem de Compra só é gerada após decisão 'Aprovado' registada em
/// `approval`". O agregado não a pode impor: não vê a requisição, não vê o
/// fornecedor, e não vê as outras ordens emitidas contra a mesma decisão.
/// </para>
/// </summary>
public sealed class IssuePurchaseOrder(IProcurementStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<IssuePurchaseOrderResult> ExecuteAsync(
        Guid requisitionId,
        Guid supplierId,
        DateOnly? issuedOn,
        DateOnly? expectedOn,
        IReadOnlyList<NewPurchaseOrderLine> lines,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var requisicao = await store.FindRequisitionAsync(requisitionId, cancellationToken);

        if (requisicao is null)
        {
            return IssuePurchaseOrderResult.RequisitionNotFound();
        }

        // A regra que este caso de uso existe para impor. Rascunho, pendente,
        // recusada e cancelada recusam todas — e a mensagem diz em que estado
        // está, porque as quatro corrigem-se de maneiras diferentes.
        if (requisicao.Status is not RequisitionStatus.Approved)
        {
            return IssuePurchaseOrderResult.RequisitionNotApproved(
                $"A requisição está em {requisicao.Status}. Só de uma requisição aprovada nasce " +
                "uma ordem de compra — sem decisão registada não se encomenda.");
        }

        var fornecedor = await store.FindSupplierAsync(supplierId, cancellationToken);

        if (fornecedor is null)
        {
            return IssuePurchaseOrderResult.SupplierNotFound();
        }

        // Desactivar um fornecedor tem de significar alguma coisa. Se ainda se
        // lhe pudesse encomendar, a desactivação era um rótulo.
        if (fornecedor.Status is not SupplierStatus.Active)
        {
            return IssuePurchaseOrderResult.SupplierInactive(
                $"O fornecedor {fornecedor.Name} está desactivado e não recebe encomendas.");
        }

        PurchaseOrder ordem;

        try
        {
            ordem = PurchaseOrder.Issue(
                requisicao.Id,
                fornecedor.Id,

                // A moeda é a da requisição, e não uma escolha de quem encomenda:
                // foi nela que o valor aprovado foi expresso, e comparar com
                // outra exigiria um câmbio que ninguém decidiu.
                requisicao.Currency,
                issuedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                expectedOn);

            foreach (var linha in lines)
            {
                ordem.AddLine(linha.Description, linha.Quantity, linha.UnitPrice);
            }
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return IssuePurchaseOrderResult.Rejected(error.Message);
        }

        if (ordem.Lines.Count == 0)
        {
            return IssuePurchaseOrderResult.Rejected(
                "Uma ordem sem linhas não encomenda nada.");
        }

        // **A invariante sobre o conjunto.** Uma requisição pode dar mais do que
        // uma ordem — dividir uma compra por dois fornecedores é legítimo — mas
        // três ordens de metade cada passariam uma a uma e, juntas, encomendavam
        // uma vez e meia o que foi aprovado. Mesma forma do `CommittedAsync` de
        // `finance`, e pela mesma razão: o agregado não vê o conjunto.
        var jaEncomendado = await store.OrderedAgainstRequisitionAsync(requisicao.Id, cancellationToken);
        var disponivel = requisicao.EstimatedTotal - jaEncomendado;

        if (ordem.Total > disponivel)
        {
            // **Recusa, e não tolerância.** Um preço acordado acima do estimado
            // acontece, e um limiar de desvio aceitável — 5%? 10%? — é decisão
            // de negócio que não está em fonte nenhuma deste repositório.
            // Inventá-lo seria abrir a alçada por um número escolhido aqui.
            //
            // Enquanto não houver quem o decida, o caminho é uma requisição
            // nova, que volta a passar por decisão. É o comportamento
            // conservador: nunca deixa sair mais do que foi aprovado.
            return IssuePurchaseOrderResult.ExceedsApproved(
                $"A requisição foi aprovada por {requisicao.EstimatedTotal:0.00} {requisicao.Currency} " +
                $"e já tem {jaEncomendado:0.00} encomendados. Restam {disponivel:0.00}, e esta ordem " +
                $"pede {ordem.Total:0.00}. Encomendar acima do aprovado precisa de nova decisão, " +
                "não de uma ordem maior.");
        }

        await store.AddOrderAsync(ordem, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.OrderIssued,
                ProcurementAuditEntityTypes.PurchaseOrder,
                ordem.Id.ToString(),
                context,
                NewValue: $$"""{"requisition":"{{ordem.RequisitionId}}","supplier":"{{fornecedor.Name}}","total":{{ordem.Total}},"currency":"{{ordem.Currency}}"}"""),
            cancellationToken);

        return IssuePurchaseOrderResult.Success(ordem.Id, ordem.Total);
    }
}

public sealed record IssuePurchaseOrderResult(
    IssuePurchaseOrderOutcome Outcome,
    Guid? PurchaseOrderId,
    decimal? Total,
    string? Error)
{
    public static IssuePurchaseOrderResult Success(Guid purchaseOrderId, decimal total) =>
        new(IssuePurchaseOrderOutcome.Issued, purchaseOrderId, total, null);

    public static IssuePurchaseOrderResult RequisitionNotFound() =>
        new(IssuePurchaseOrderOutcome.RequisitionNotFound, null, null, "Requisição não encontrada.");

    public static IssuePurchaseOrderResult SupplierNotFound() =>
        new(IssuePurchaseOrderOutcome.SupplierNotFound, null, null, "Fornecedor não encontrado.");

    public static IssuePurchaseOrderResult RequisitionNotApproved(string error) =>
        new(IssuePurchaseOrderOutcome.RequisitionNotApproved, null, null, error);

    public static IssuePurchaseOrderResult SupplierInactive(string error) =>
        new(IssuePurchaseOrderOutcome.SupplierInactive, null, null, error);

    public static IssuePurchaseOrderResult ExceedsApproved(string error) =>
        new(IssuePurchaseOrderOutcome.ExceedsApproved, null, null, error);

    public static IssuePurchaseOrderResult Rejected(string error) =>
        new(IssuePurchaseOrderOutcome.Rejected, null, null, error);
}

public enum IssuePurchaseOrderOutcome
{
    Issued,
    RequisitionNotFound,
    SupplierNotFound,

    /// <summary>
    /// A requisição não está aprovada. <strong>409</strong> — é o estado que
    /// impede, e corrige-se submetendo e esperando pela decisão.
    /// </summary>
    RequisitionNotApproved,

    /// <summary>Fornecedor desactivado. 409.</summary>
    SupplierInactive,

    /// <summary>
    /// O total encomendado passaria o aprovado. <strong>409</strong>: não é
    /// pedido inválido, é alçada esgotada.
    /// </summary>
    ExceedsApproved,

    Rejected,
}

public sealed class ListPurchaseOrders(IProcurementStore store)
{
    public async Task<IReadOnlyList<PurchaseOrderView>> ExecuteAsync(
        Guid? requisitionId,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        var ordens = await store.ListOrdersAsync(requisitionId, supplierId, cancellationToken);

        var vistas = new List<PurchaseOrderView>(ordens.Count);

        foreach (var ordem in ordens)
        {
            var fornecedor = await store.FindSupplierAsync(ordem.SupplierId, cancellationToken);

            // O nome vem do fornecedor a cada leitura, e não de uma cópia
            // guardada na ordem: uma cópia ficava obsoleta em silêncio (BR-18).
            vistas.Add(PurchaseOrderViews.ToView(ordem, fornecedor?.Name ?? "(fornecedor removido)"));
        }

        return vistas;
    }
}

public sealed class GetPurchaseOrder(IProcurementStore store)
{
    public async Task<PurchaseOrderView?> ExecuteAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var ordem = await store.FindOrderAsync(purchaseOrderId, cancellationToken);

        if (ordem is null)
        {
            return null;
        }

        var fornecedor = await store.FindSupplierAsync(ordem.SupplierId, cancellationToken);

        return PurchaseOrderViews.ToView(ordem, fornecedor?.Name ?? "(fornecedor removido)");
    }
}

/// <summary>
/// Cancela uma ordem de compra. Nunca elimina — BR-14.
/// </summary>
public sealed class CancelPurchaseOrder(IProcurementStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelPurchaseOrderResult> ExecuteAsync(
        Guid purchaseOrderId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var ordem = await store.FindOrderForUpdateAsync(purchaseOrderId, cancellationToken);

        if (ordem is null)
        {
            return CancelPurchaseOrderResult.NotFound();
        }

        try
        {
            ordem.Cancel(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelPurchaseOrderResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProcurementAuditActions.OrderCancelled,
                ProcurementAuditEntityTypes.PurchaseOrder,
                ordem.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{ordem.CancellationReason}}"}"""),
            cancellationToken);

        return CancelPurchaseOrderResult.Success();
    }
}

public sealed record CancelPurchaseOrderResult(CancelPurchaseOrderOutcome Outcome, string? Error)
{
    public static CancelPurchaseOrderResult Success() => new(CancelPurchaseOrderOutcome.Cancelled, null);

    public static CancelPurchaseOrderResult NotFound() =>
        new(CancelPurchaseOrderOutcome.NotFound, "Ordem de compra não encontrada.");

    public static CancelPurchaseOrderResult Rejected(string error) =>
        new(CancelPurchaseOrderOutcome.Rejected, error);
}

public enum CancelPurchaseOrderOutcome
{
    Cancelled,
    NotFound,
    Rejected,
}
