using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Domain.Tests;

public class VehicleTests
{
    private static readonly DateOnly Hoje = new(2026, 8, 30);

    private static Vehicle Registada() => Vehicle.Register("LD-45-67-AB", "Toyota Hilux");

    // --- Register / Deactivate ---------------------------------------------

    [Fact]
    public void Register_StartsAsActive()
    {
        var viatura = Registada();

        Assert.Equal(VehicleStatus.Active, viatura.Status);
        Assert.Empty(viatura.Maintenances);
        Assert.Empty(viatura.Assignments);
    }

    [Fact]
    public void Register_NormalizesPlateNumber()
    {
        var viatura = Vehicle.Register("  ld-45-67-ab  ", "Toyota Hilux");

        Assert.Equal("LD-45-67-AB", viatura.PlateNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutPlateNumber_Throws(string plateNumber)
    {
        Assert.Throws<ArgumentException>(() => Vehicle.Register(plateNumber, "Toyota Hilux"));
    }

    [Fact]
    public void Deactivate_SetsInactive()
    {
        var viatura = Registada();

        viatura.Deactivate();

        Assert.Equal(VehicleStatus.Inactive, viatura.Status);
    }

    // --- Manutenção ----------------------------------------------------

    [Fact]
    public void OpenMaintenance_SetsVehicleInMaintenance()
    {
        var viatura = Registada();

        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão dos 20.000 km", Hoje);

        Assert.Equal(VehicleStatus.InMaintenance, viatura.Status);
        Assert.Equal(viatura.Id, registo.VehicleId);
        Assert.Equal(MaintenanceType.Preventive, registo.Type);
        Assert.Equal(Hoje, registo.StartedOn);
        Assert.True(registo.IsOpen);
        Assert.Same(registo, Assert.Single(viatura.Maintenances));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void OpenMaintenance_WithoutDescription_Throws(string description)
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(
            () => viatura.OpenMaintenance(MaintenanceType.Corrective, description, Hoje));
    }

    [Fact]
    public void OpenMaintenance_WhileAlreadyInMaintenance_Throws()
    {
        var viatura = Registada();
        viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);

        Assert.Throws<InvalidOperationException>(
            () => viatura.OpenMaintenance(MaintenanceType.Corrective, "Outra avaria", Hoje));
    }

    [Fact]
    public void OpenMaintenance_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(
            () => viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje));
    }

    [Fact]
    public void CloseMaintenance_ReturnsVehicleToActive()
    {
        var viatura = Registada();
        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);
        var fim = Hoje.AddDays(2);

        viatura.CloseMaintenance(registo.Id, fim);

        Assert.Equal(VehicleStatus.Active, viatura.Status);
        Assert.False(registo.IsOpen);
        Assert.Equal(fim, registo.EndedOn);
    }

    [Fact]
    public void CloseMaintenance_AlreadyClosed_Throws()
    {
        var viatura = Registada();
        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);
        viatura.CloseMaintenance(registo.Id, Hoje.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => viatura.CloseMaintenance(registo.Id, Hoje.AddDays(2)));
    }

    [Fact]
    public void CloseMaintenance_EndedBeforeStarted_Throws()
    {
        var viatura = Registada();
        var registo = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);

        Assert.Throws<ArgumentException>(() => viatura.CloseMaintenance(registo.Id, Hoje.AddDays(-1)));
    }

    [Fact]
    public void CloseMaintenance_UnknownId_Throws()
    {
        var viatura = Registada();

        Assert.Throws<InvalidOperationException>(() => viatura.CloseMaintenance(Guid.CreateVersion7(), Hoje));
    }

    [Fact]
    public void AfterMaintenanceClosed_CanOpenANewOne()
    {
        // Uma segunda avaria depois de a primeira fechar é legítima — só não
        // pode haver duas abertas ao mesmo tempo.
        var viatura = Registada();
        var primeiro = viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);
        viatura.CloseMaintenance(primeiro.Id, Hoje.AddDays(1));

        var segundo = viatura.OpenMaintenance(MaintenanceType.Corrective, "Travões", Hoje.AddDays(5));

        Assert.Equal(2, viatura.Maintenances.Count);
        Assert.True(segundo.IsOpen);
    }

    // --- Atribuição ------------------------------------------------------

    [Fact]
    public void Assign_AddsOpenAssignment()
    {
        var viatura = Registada();
        var motoristaId = Guid.CreateVersion7();

        var atribuicao = viatura.Assign(motoristaId, Hoje);

        Assert.Equal(viatura.Id, atribuicao.VehicleId);
        Assert.Equal(motoristaId, atribuicao.EmployeeId);
        Assert.Equal(Hoje, atribuicao.StartedOn);
        Assert.True(atribuicao.IsOpen);
        Assert.Same(atribuicao, Assert.Single(viatura.Assignments));
    }

    [Fact]
    public void Assign_WithoutEmployee_Throws()
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.Assign(Guid.Empty, Hoje));
    }

    [Fact]
    public void Assign_WhileAlreadyAssigned_Throws()
    {
        var viatura = Registada();
        viatura.Assign(Guid.CreateVersion7(), Hoje);

        Assert.Throws<InvalidOperationException>(() => viatura.Assign(Guid.CreateVersion7(), Hoje));
    }

    [Fact]
    public void Assign_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(() => viatura.Assign(Guid.CreateVersion7(), Hoje));
    }

    [Fact]
    public void Assign_DuringMaintenance_IsAllowed()
    {
        // Frota e manutenção não se excluem: uma viatura pode ter motorista
        // atribuído e ir para revisão sem que isso desatribua ninguém.
        var viatura = Registada();
        viatura.OpenMaintenance(MaintenanceType.Preventive, "Revisão", Hoje);

        var atribuicao = viatura.Assign(Guid.CreateVersion7(), Hoje);

        Assert.True(atribuicao.IsOpen);
    }

    [Fact]
    public void EndAssignment_ClosesIt()
    {
        var viatura = Registada();
        var atribuicao = viatura.Assign(Guid.CreateVersion7(), Hoje);
        var fim = Hoje.AddDays(10);

        viatura.EndAssignment(atribuicao.Id, fim);

        Assert.False(atribuicao.IsOpen);
        Assert.Equal(fim, atribuicao.EndedOn);
    }

    [Fact]
    public void EndAssignment_AlreadyEnded_Throws()
    {
        var viatura = Registada();
        var atribuicao = viatura.Assign(Guid.CreateVersion7(), Hoje);
        viatura.EndAssignment(atribuicao.Id, Hoje.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => viatura.EndAssignment(atribuicao.Id, Hoje.AddDays(2)));
    }

    [Fact]
    public void EndAssignment_EndedBeforeStarted_Throws()
    {
        var viatura = Registada();
        var atribuicao = viatura.Assign(Guid.CreateVersion7(), Hoje);

        Assert.Throws<ArgumentException>(() => viatura.EndAssignment(atribuicao.Id, Hoje.AddDays(-1)));
    }

    [Fact]
    public void EndAssignment_UnknownId_Throws()
    {
        var viatura = Registada();

        Assert.Throws<InvalidOperationException>(() => viatura.EndAssignment(Guid.CreateVersion7(), Hoje));
    }

    [Fact]
    public void AfterAssignmentEnded_CanAssignAnotherDriver()
    {
        var viatura = Registada();
        var primeiro = viatura.Assign(Guid.CreateVersion7(), Hoje);
        viatura.EndAssignment(primeiro.Id, Hoje.AddDays(3));

        var segundo = viatura.Assign(Guid.CreateVersion7(), Hoje.AddDays(4));

        Assert.Equal(2, viatura.Assignments.Count);
        Assert.True(segundo.IsOpen);
    }
}
