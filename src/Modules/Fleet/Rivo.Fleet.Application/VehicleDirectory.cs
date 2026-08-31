using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Contracts;

namespace Rivo.Fleet.Application;

/// <summary>Implementa o contrato publicado: leitura de Viatura para consumidores externos.</summary>
public sealed class VehicleDirectory(IVehicleStore store) : IVehicleDirectory
{
    public async Task<VehicleReference?> FindAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        var viatura = await store.FindAsync(vehicleId, cancellationToken);

        return viatura is null
            ? null
            : new VehicleReference(viatura.Id, viatura.PlateNumber, viatura.Model, viatura.Status.ToString());
    }
}
