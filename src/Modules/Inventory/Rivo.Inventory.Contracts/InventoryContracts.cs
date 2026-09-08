namespace Rivo.Inventory.Contracts;

/// <summary>
/// Superfície publicada de `inventory`. Assembly sem dependências (ADR-017).
///
/// <para>
/// Só o catálogo de permissões, por agora — sem consumidor ainda para um
/// contrato de leitura. Ver a nota equivalente em `Rivo.Projects.Contracts`.
/// </para>
/// </summary>
public static class InventoryPermissions
{
    public const string ItemsRead = "inventory.items.read";
    public const string ItemsWrite = "inventory.items.write";

    public static readonly IReadOnlyList<string> All = [ItemsRead, ItemsWrite];
}

/// <summary>
/// O catálogo de artigos, publicado. Primeiro consumidor: a secção
/// <c>Product</c> do SAF-T AO.
/// </summary>
public interface IInventoryCatalogue
{
    /// <summary>
    /// Todos os artigos, activos e inactivos.
    ///
    /// <para>
    /// <strong>Inclui os inactivos</strong>, pela mesma razão de
    /// <c>ICustomerDirectory.ListAllAsync</c>: um artigo desactivado hoje pode
    /// ter sido facturado em Março, e o XSD exige que os documentos
    /// referenciem master data presente no ficheiro
    /// (<c>ProductCodeConstraint</c>).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CatalogueItem>> ListAllAsync(CancellationToken cancellationToken);
}

/// <param name="Sku">Referência do artigo, única e em maiúsculas.</param>
/// <param name="Unit">
/// Unidade de medida. Não vai no SAF-T — <c>Product</c> não tem campo para
/// ela —, mas faz parte da identidade do artigo para quem lê o contrato.
/// </param>
public sealed record CatalogueItem(Guid ItemId, string Sku, string Name, string Unit);

/// <summary>
/// Leitura agregada de valorização de stock para composição (Analytics &amp;
/// IA, módulo 10) — valor corrente e valorização por período, para todo o
/// inventário, não por item.
/// </summary>
public interface IInventoryValuationOverview
{
    /// <summary>Valor do stock agora — soma de <c>QuantityOnHand × AverageCost</c> sobre os itens activos.</summary>
    Task<decimal> GetCurrentStockValueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Valorização no período: soma, com sinal, do valor movimentado
    /// (entradas menos saídas) — não é uma posição a uma data, é o que
    /// entrou e saiu de valor na janela.
    /// </summary>
    Task<decimal> GetPeriodValuationAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
