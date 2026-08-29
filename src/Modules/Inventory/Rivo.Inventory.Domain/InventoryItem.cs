namespace Rivo.Inventory.Domain;

/// <summary>
/// Item de inventário. Esqueleto do módulo — ver `modules/inventory.md`.
///
/// <para>
/// <strong>Fatia mínima, deliberada.</strong> Armazém, Movimento,
/// Transferência, Contagem e valorização de stock (ver `modules/inventory.md`
/// §Possui) ficam por fazer. Esta entidade é só o catálogo — SKU, nome e a
/// quantidade em mão, sem movimento nenhum a alterá-la ainda.
/// </para>
/// </summary>
public sealed class InventoryItem
{
    private InventoryItem(Guid id, string sku, string name, string unit)
    {
        Id = id;
        Sku = sku;
        Name = name;
        Unit = unit;
        QuantityOnHand = 0m;
        Status = InventoryItemStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private InventoryItem()
    {
        Sku = string.Empty;
        Name = string.Empty;
        Unit = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Referência do item. Normalizada em maiúsculas.</summary>
    public string Sku { get; private set; }

    public string Name { get; private set; }

    /// <summary>Unidade de medida — "un", "kg", "l", conforme o item.</summary>
    public string Unit { get; private set; }

    /// <summary>
    /// Quantidade em mão. **Sem movimento a alterá-la ainda** — nasce a zero
    /// e fica a zero até `Movimento` existir.
    /// </summary>
    public decimal QuantityOnHand { get; private set; }

    public InventoryItemStatus Status { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static InventoryItem Register(string sku, string name, string unit)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Um item precisa de referência (SKU).", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um item precisa de nome.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("Um item precisa de unidade de medida.", nameof(unit));
        }

        return new InventoryItem(
            Guid.CreateVersion7(), sku.Trim().ToUpperInvariant(), name.Trim(), unit.Trim());
    }

    /// <summary>Desactiva o item. Nunca eliminar — pode estar referenciado por recepções.</summary>
    public void Deactivate()
    {
        Status = InventoryItemStatus.Inactive;
    }

    public void Reactivate()
    {
        Status = InventoryItemStatus.Active;
    }
}

public enum InventoryItemStatus
{
    Active,
    Inactive,
}
