namespace Rivo.Fleet.Domain;

/// <summary>
/// Viatura. Esqueleto do módulo — ver `modules/fleet.md`.
///
/// <para>
/// <strong>Fatia mínima, deliberada.</strong> Manutenção, Plano de
/// Manutenção, Atribuição, Registo de Viagem, Despesa de Frota e Seguros (ver
/// `modules/fleet.md` §Possui) ficam por fazer. Esta entidade é só a matrícula
/// e o modelo, sem nada disso ligado ainda.
/// </para>
/// </summary>
public sealed class Vehicle
{
    private Vehicle(Guid id, string plateNumber, string model)
    {
        Id = id;
        PlateNumber = plateNumber;
        Model = model;
        Status = VehicleStatus.Active;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Vehicle()
    {
        PlateNumber = string.Empty;
        Model = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Matrícula, normalizada em maiúsculas.</summary>
    public string PlateNumber { get; private set; }

    public string Model { get; private set; }

    public VehicleStatus Status { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    public static Vehicle Register(string plateNumber, string model)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            throw new ArgumentException("Uma viatura precisa de matrícula.", nameof(plateNumber));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Uma viatura precisa de modelo.", nameof(model));
        }

        return new Vehicle(
            Guid.CreateVersion7(), plateNumber.Trim().ToUpperInvariant(), model.Trim());
    }

    /// <summary>Envia para manutenção. Nunca elimina — o histórico da viatura fica.</summary>
    public void SendToMaintenance()
    {
        if (Status is VehicleStatus.Inactive)
        {
            throw new InvalidOperationException("Uma viatura inactiva não vai para manutenção.");
        }

        Status = VehicleStatus.InMaintenance;
    }

    public void ReturnFromMaintenance()
    {
        if (Status is not VehicleStatus.InMaintenance)
        {
            throw new InvalidOperationException("Esta viatura não está em manutenção.");
        }

        Status = VehicleStatus.Active;
    }

    public void Deactivate()
    {
        Status = VehicleStatus.Inactive;
    }
}

public enum VehicleStatus
{
    Active,
    InMaintenance,
    Inactive,
}
