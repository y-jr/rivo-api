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

    // --- Plano de Manutenção ---------------------------------------------

    [Fact]
    public void SchedulePlan_AddsActivePlan()
    {
        var viatura = Registada();
        var devido = Hoje.AddDays(90);

        var plano = viatura.SchedulePlan("Mudança de óleo", 90, devido);

        Assert.Equal(viatura.Id, plano.VehicleId);
        Assert.Equal(90, plano.IntervalDays);
        Assert.Equal(devido, plano.NextDueOn);
        Assert.True(plano.IsActive);
        Assert.Same(plano, Assert.Single(viatura.Plans));
    }

    [Fact]
    public void SchedulePlan_MultiplePlansAreNormal()
    {
        // Ao contrário de Manutenção e Atribuição, vários planos activos ao
        // mesmo tempo não se excluem.
        var viatura = Registada();

        viatura.SchedulePlan("Óleo", 90, Hoje.AddDays(90));
        viatura.SchedulePlan("Pneus", 180, Hoje.AddDays(180));

        Assert.Equal(2, viatura.Plans.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SchedulePlan_WithoutDescription_Throws(string description)
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.SchedulePlan(description, 90, Hoje.AddDays(90)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SchedulePlan_NonPositiveInterval_Throws(int intervalDays)
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.SchedulePlan("Óleo", intervalDays, Hoje.AddDays(90)));
    }

    [Fact]
    public void SchedulePlan_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(() => viatura.SchedulePlan("Óleo", 90, Hoje.AddDays(90)));
    }

    [Fact]
    public void IsOverdue_PastDueDate_IsTrue()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);

        Assert.True(plano.IsOverdue(Hoje.AddDays(1)));
        Assert.False(plano.IsOverdue(Hoje));
        Assert.False(plano.IsOverdue(Hoje.AddDays(-1)));
    }

    [Fact]
    public void CompletePlanCycle_ReschedulesFromCompletionDate()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        var concluidoEm = Hoje.AddDays(5);

        viatura.CompletePlanCycle(plano.Id, concluidoEm);

        // Reagenda a partir de quando foi concluído, não da data que estava
        // marcada — não empilha ciclos em atraso.
        Assert.Equal(concluidoEm.AddDays(90), plano.NextDueOn);
    }

    [Fact]
    public void CompletePlanCycle_OnCancelledPlan_Throws()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        viatura.CancelPlan(plano.Id);

        Assert.Throws<InvalidOperationException>(() => viatura.CompletePlanCycle(plano.Id, Hoje));
    }

    [Fact]
    public void CompletePlanCycle_UnknownId_Throws()
    {
        var viatura = Registada();

        Assert.Throws<InvalidOperationException>(() => viatura.CompletePlanCycle(Guid.CreateVersion7(), Hoje));
    }

    [Fact]
    public void CompletePlanCycle_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(() => viatura.CompletePlanCycle(plano.Id, Hoje));
    }

    [Fact]
    public void CancelPlan_DeactivatesIt()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);

        viatura.CancelPlan(plano.Id);

        Assert.False(plano.IsActive);
    }

    [Fact]
    public void CancelPlan_StopsCountingAsOverdue()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        viatura.CancelPlan(plano.Id);

        Assert.False(plano.IsOverdue(Hoje.AddDays(365)));
    }

    [Fact]
    public void CancelPlan_AlreadyCancelled_Throws()
    {
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        viatura.CancelPlan(plano.Id);

        Assert.Throws<InvalidOperationException>(() => viatura.CancelPlan(plano.Id));
    }

    [Fact]
    public void CancelPlan_UnknownId_Throws()
    {
        var viatura = Registada();

        Assert.Throws<InvalidOperationException>(() => viatura.CancelPlan(Guid.CreateVersion7()));
    }

    [Fact]
    public void CancelPlan_OnInactiveVehicle_IsAllowed()
    {
        // Cancelar os planos de uma viatura que acabou de ficar inactiva é o
        // que se espera, não algo a bloquear.
        var viatura = Registada();
        var plano = viatura.SchedulePlan("Óleo", 90, Hoje);
        viatura.Deactivate();

        viatura.CancelPlan(plano.Id);

        Assert.False(plano.IsActive);
    }

    // --- Registo de Viagem ------------------------------------------------

    [Fact]
    public void RegisterTrip_RecordsDistanceAndDriver()
    {
        var viatura = Registada();
        var motorista = Guid.CreateVersion7();

        var viagem = viatura.RegisterTrip(motorista, Hoje, Hoje, 1000m, 1080m, "Entrega em Viana");

        Assert.Equal(motorista, viagem.DriverId);
        Assert.Equal(80m, viagem.Distance);
        Assert.Equal("Entrega em Viana", viagem.Purpose);
        Assert.Same(viagem, Assert.Single(viatura.Trips));
    }

    [Fact]
    public void RegisterTrip_WithoutDriver_IsAllowed()
    {
        var viatura = Registada();

        var viagem = viatura.RegisterTrip(null, Hoje, Hoje, 1000m, 1050m, null);

        Assert.Null(viagem.DriverId);
        Assert.Equal(50m, viagem.Distance);
    }

    [Fact]
    public void RegisterTrip_WithEmptyDriverId_Throws()
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.RegisterTrip(Guid.Empty, Hoje, Hoje, 1000m, 1050m, null));
    }

    [Fact]
    public void RegisterTrip_EndedBeforeStarted_Throws()
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(
            () => viatura.RegisterTrip(null, Hoje, Hoje.AddDays(-1), 1000m, 1050m, null));
    }

    [Fact]
    public void RegisterTrip_NegativeStartOdometer_Throws()
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.RegisterTrip(null, Hoje, Hoje, -1m, 50m, null));
    }

    [Fact]
    public void RegisterTrip_EndOdometerBeforeStart_Throws()
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.RegisterTrip(null, Hoje, Hoje, 1000m, 900m, null));
    }

    [Fact]
    public void RegisterTrip_ExactlySameOdometer_HasZeroDistance()
    {
        var viatura = Registada();

        var viagem = viatura.RegisterTrip(null, Hoje, Hoje, 1000m, 1000m, null);

        Assert.Equal(0m, viagem.Distance);
    }

    [Fact]
    public void RegisterTrip_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(() => viatura.RegisterTrip(null, Hoje, Hoje, 1000m, 1050m, null));
    }

    // --- Despesa de Frota ---------------------------------------------------

    [Fact]
    public void RegisterExpense_RecordsCategoryAndAmount()
    {
        var viatura = Registada();

        var despesa = viatura.RegisterExpense(FleetExpenseCategory.Fuel, 15000m, Hoje, "Posto Sonangol");

        Assert.Equal(FleetExpenseCategory.Fuel, despesa.Category);
        Assert.Equal(15000m, despesa.Amount);
        Assert.Equal("Posto Sonangol", despesa.Description);
        Assert.Same(despesa, Assert.Single(viatura.Expenses));
    }

    [Theory]
    [InlineData(FleetExpenseCategory.Fuel)]
    [InlineData(FleetExpenseCategory.Toll)]
    [InlineData(FleetExpenseCategory.Parking)]
    public void RegisterExpense_AcceptsAllThreeCategories(FleetExpenseCategory categoria)
    {
        var viatura = Registada();

        var despesa = viatura.RegisterExpense(categoria, 1000m, Hoje, null);

        Assert.Equal(categoria, despesa.Category);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RegisterExpense_NonPositiveAmount_Throws(decimal valor)
    {
        var viatura = Registada();

        Assert.Throws<ArgumentException>(() => viatura.RegisterExpense(FleetExpenseCategory.Toll, valor, Hoje, null));
    }

    [Fact]
    public void RegisterExpense_OnInactiveVehicle_Throws()
    {
        var viatura = Registada();
        viatura.Deactivate();

        Assert.Throws<InvalidOperationException>(
            () => viatura.RegisterExpense(FleetExpenseCategory.Parking, 500m, Hoje, null));
    }
}
