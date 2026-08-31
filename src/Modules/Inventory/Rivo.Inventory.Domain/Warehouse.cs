namespace Rivo.Inventory.Domain;

/// <summary>
/// Armazém — agregado raiz próprio de `inventory` (`modules/inventory.md`
/// §Possui), independente de <see cref="InventoryItem"/>: existe antes e
/// depois de conter qualquer stock, e não é filho de nenhum item.
///
/// <para>
/// Um movimento de stock guarda apenas o <c>WarehouseId</c>
/// (<see cref="StockMovement.WarehouseId"/>) — nunca uma referência de
/// objecto directa. Mesma disciplina inter-agregado usada em todo o Rivo
/// (ADR-010), aqui dentro do mesmo módulo em vez de entre módulos.
/// </para>
/// </summary>
public sealed class Warehouse
{
    private Warehouse(Guid id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
        Status = WarehouseStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Código do armazém. Normalizado em maiúsculas.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public WarehouseStatus Status { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Warehouse Register(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Um armazém precisa de código.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um armazém precisa de nome.", nameof(name));
        }

        return new Warehouse(Guid.CreateVersion7(), code.Trim().ToUpperInvariant(), name.Trim());
    }

    /// <summary>Desactiva o armazém. Nunca eliminar — pode estar referenciado por movimentos.</summary>
    public void Deactivate()
    {
        Status = WarehouseStatus.Inactive;
    }

    public void Reactivate()
    {
        Status = WarehouseStatus.Active;
    }
}

public enum WarehouseStatus
{
    Active,
    Inactive,
}
