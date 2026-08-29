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
