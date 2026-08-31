using Rivo.Inventory.Application.Abstractions;

namespace Rivo.Inventory.Application.UseCases;

/// <summary>
/// Valorização de stock por período — soma de <c>StockMovement.Value</c>
/// (`modules/inventory.md` §Contratos publicados), para cada item que teve
/// pelo menos um movimento na janela.
///
/// <para>
/// **Não reconstrói a quantidade num ponto do passado** — só soma o valor
/// movimentado dentro do intervalo, o que responde "quanto valor entrou e
/// saiu de stock neste período", não "quanto valia o stock a uma data
/// concreta". Um relatório de posição num instante exigiria reconstruir a
/// quantidade a partir dos movimentos até essa data, que não é o que este
/// caso de uso faz — não se inventa agora, sem um consumidor real a pedi-lo.
/// </para>
///
/// <para>
/// Leitura pura, sem entidade de domínio a sair desta camada — mesma
/// disciplina das outras vistas.
/// </para>
/// </summary>
public sealed class GetStockValuationByPeriod(IInventoryItemStore store)
{
    public async Task<StockValuationResult> ExecuteAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return StockValuationResult.Rejected("A data inicial não pode ser posterior à data final.");
        }

        var itens = await store.ListAsync(includeInactive: true, cancellationToken);

        // Só itens com movimento na janela -- um item sem nenhum não tem
        // nada a dizer sobre este período, e listá-lo a zero seria ruído.
        var entradas = itens
            .Where(item => item.Movements.Any(m => m.OccurredOn >= from && m.OccurredOn <= to))
            .Select(item => new StockValuationEntry(
                item.Id,
                item.Sku,
                item.Name,
                item.Movements.Where(m => m.OccurredOn >= from && m.OccurredOn <= to).Sum(m => m.Value)))
            .OrderBy(entrada => entrada.Sku)
            .ToList();

        return StockValuationResult.Success(entradas);
    }
}

public sealed record StockValuationEntry(Guid ItemId, string Sku, string Name, decimal PeriodValue);

public sealed record StockValuationResult(StockValuationOutcome Outcome, IReadOnlyList<StockValuationEntry>? Entries, string? Error)
{
    public static StockValuationResult Success(IReadOnlyList<StockValuationEntry> entries) =>
        new(StockValuationOutcome.Computed, entries, null);

    public static StockValuationResult Rejected(string error) =>
        new(StockValuationOutcome.Rejected, null, error);
}

public enum StockValuationOutcome
{
    Computed,

    /// <summary>Janela invertida (data inicial depois da final). 400.</summary>
    Rejected,
}
