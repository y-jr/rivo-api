using Rivo.Projects.Domain;

namespace Rivo.Projects.Domain.Tests;

/// <summary>
/// Alocação de Recursos — distinta da atribuição de Tarefa (ver o
/// comentário em <see cref="ProjectResourceAllocation"/>). O agregado só
/// impõe as invariantes que não dependem de outro módulo: existência do
/// Colaborador/Viatura é responsabilidade da Application (ADR-010).
/// </summary>
public class ProjectResourceAllocationTests
{
    private static readonly DateOnly Inicio = new(2026, 8, 1);
    private static readonly Guid Colaborador = Guid.CreateVersion7();
    private static readonly Guid OutroColaborador = Guid.CreateVersion7();
    private static readonly Guid Viatura = Guid.CreateVersion7();

    private static Project Aberto() => Project.Open("Renovação do armazém", Inicio);

    [Fact]
    public void AllocateResource_KeepsKindResourceIdAndStartDate()
    {
        var projecto = Aberto();

        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        Assert.Equal(ResourceKind.Employee, alocacao.Kind);
        Assert.Equal(Colaborador, alocacao.ResourceId);
        Assert.Equal(Inicio, alocacao.StartsOn);
        Assert.Null(alocacao.EndsOn);
        Assert.True(alocacao.IsOpen);
    }

    [Fact]
    public void AllocateResource_AddsToProjectAllocations()
    {
        var projecto = Aberto();
        projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        Assert.Single(projecto.Allocations);
    }

    [Fact]
    public void AllocateResource_EmployeeAndVehicle_BothAllowedSimultaneously()
    {
        var projecto = Aberto();
        projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        projecto.AllocateResource(ResourceKind.Vehicle, Viatura, Inicio);

        Assert.Equal(2, projecto.Allocations.Count);
    }

    [Fact]
    public void AllocateResource_WithEmptyResourceId_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<ArgumentException>(() =>
            projecto.AllocateResource(ResourceKind.Employee, Guid.Empty, Inicio));
    }

    [Fact]
    public void AllocateResource_BeforeProjectStart_Throws()
    {
        var projecto = Aberto();
        var antesDoInicio = Inicio.AddDays(-1);

        Assert.Throws<ArgumentException>(() =>
            projecto.AllocateResource(ResourceKind.Employee, Colaborador, antesDoInicio));
    }

    /// <summary>
    /// O mesmo recurso duas vezes, aberto, seria dupla contagem de
    /// capacidade — mesma leitura de <c>Vehicle.Assign</c> em `fleet`.
    /// </summary>
    [Fact]
    public void AllocateResource_SameOpenResourceTwice_Throws()
    {
        var projecto = Aberto();
        projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        Assert.Throws<InvalidOperationException>(() =>
            projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio));
    }

    [Fact]
    public void AllocateResource_SameResourceDifferentKind_IsAllowed()
    {
        // Kinds diferentes nunca colidem, mesmo com o mesmo Guid por
        // coincidência -- Employee e Vehicle são espaços de identificador
        // distintos, nunca o mesmo recurso.
        var projecto = Aberto();
        projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        var alocacao = projecto.AllocateResource(ResourceKind.Vehicle, Colaborador, Inicio);

        Assert.Equal(ResourceKind.Vehicle, alocacao.Kind);
    }

    [Fact]
    public void AllocateResource_DifferentEmployees_BothAllowed()
    {
        var projecto = Aberto();
        projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        projecto.AllocateResource(ResourceKind.Employee, OutroColaborador, Inicio);

        Assert.Equal(2, projecto.Allocations.Count);
    }

    [Fact]
    public void AllocateResource_AfterEndingPrevious_IsAllowedAgain()
    {
        var projecto = Aberto();
        var primeira = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        projecto.EndResourceAllocation(primeira.Id, Inicio.AddDays(10));

        var segunda = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio.AddDays(15));

        Assert.Equal(2, projecto.Allocations.Count);
        Assert.True(segunda.IsOpen);
    }

    [Fact]
    public void AllocateResource_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        projecto.Close(Inicio.AddDays(30));

        Assert.Throws<InvalidOperationException>(() =>
            projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio));
    }

    [Fact]
    public void EndResourceAllocation_SetsEndDateAndClosesIt()
    {
        var projecto = Aberto();
        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        var fim = Inicio.AddDays(20);

        projecto.EndResourceAllocation(alocacao.Id, fim);

        Assert.Equal(fim, alocacao.EndsOn);
        Assert.False(alocacao.IsOpen);
    }

    [Fact]
    public void EndResourceAllocation_Twice_Throws()
    {
        var projecto = Aberto();
        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        projecto.EndResourceAllocation(alocacao.Id, Inicio.AddDays(5));

        Assert.Throws<InvalidOperationException>(() =>
            projecto.EndResourceAllocation(alocacao.Id, Inicio.AddDays(10)));
    }

    [Fact]
    public void EndResourceAllocation_BeforeStartDate_Throws()
    {
        var projecto = Aberto();
        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        Assert.Throws<ArgumentException>(() =>
            projecto.EndResourceAllocation(alocacao.Id, Inicio.AddDays(-1)));
    }

    [Fact]
    public void EndResourceAllocation_NotFound_Throws()
    {
        var projecto = Aberto();

        Assert.Throws<InvalidOperationException>(() =>
            projecto.EndResourceAllocation(Guid.CreateVersion7(), Inicio));
    }

    [Fact]
    public void EndResourceAllocation_OnClosedProject_Throws()
    {
        var projecto = Aberto();
        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);
        projecto.Close(Inicio.AddDays(30));

        Assert.Throws<InvalidOperationException>(() =>
            projecto.EndResourceAllocation(alocacao.Id, Inicio.AddDays(10)));
    }

    [Fact]
    public void DomainNeverTouchesConcurrencyCounter()
    {
        var projecto = Aberto();
        var alocacao = projecto.AllocateResource(ResourceKind.Employee, Colaborador, Inicio);

        Assert.Equal(0, alocacao.Version);
    }
}
