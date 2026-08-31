using Rivo.Audit.Contracts;
using Rivo.Fleet.Contracts;
using Rivo.Hr.Contracts;
using Rivo.Projects.Application.Abstractions;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Application.UseCases;

/// <summary>
/// Aloca um recurso — Colaborador ou Viatura — a um projecto.
///
/// <para>
/// <strong>O recurso tem de existir no módulo dono</strong> (ADR-010):
/// Colaborador em `hr`, Viatura em `fleet` — lido pelo contrato publicado
/// de cada um, nunca copiado (BR-18). `projects` não sabe nada de
/// matrícula, modelo, nome ou departamento.
/// </para>
/// </summary>
public sealed class AllocateProjectResource(
    IProjectStore store,
    IEmployeeDirectory employees,
    IVehicleDirectory vehicles,
    IAuditTrail audit,
    TimeProvider clock)
{
    public async Task<AllocateResourceResult> ExecuteAsync(
        Guid projectId,
        ResourceKind kind,
        Guid resourceId,
        DateOnly startsOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return AllocateResourceResult.ProjectNotFound();
        }

        var existe = kind switch
        {
            ResourceKind.Employee => await employees.FindAsync(resourceId, clock.GetUtcNow(), cancellationToken) is not null,
            ResourceKind.Vehicle => await vehicles.FindAsync(resourceId, cancellationToken) is not null,
            _ => false,
        };

        if (!existe)
        {
            return AllocateResourceResult.ResourceNotFound();
        }

        ProjectResourceAllocation alocacao;

        try
        {
            alocacao = projecto.AllocateResource(kind, resourceId, startsOn);
        }
        catch (ArgumentException error)
        {
            // Campo mal preenchido — recurso vazio, ou data antes do início
            // do projecto. 400.
            return AllocateResourceResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Conflito com o estado actual — projecto fechado, ou o mesmo
            // recurso já está alocado e aberto. 409.
            return AllocateResourceResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.ResourceAllocated,
                ProjectsAuditEntityTypes.ResourceAllocation,
                alocacao.Id.ToString(),
                context,
                NewValue: $$"""{"projectId":"{{projectId}}","kind":"{{kind}}","resourceId":"{{resourceId}}","startsOn":"{{startsOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return AllocateResourceResult.Success(alocacao.Id);
    }
}

public sealed record AllocateResourceResult(AllocateResourceOutcome Outcome, Guid? AllocationId, string? Error)
{
    public static AllocateResourceResult Success(Guid allocationId) =>
        new(AllocateResourceOutcome.Allocated, allocationId, null);

    public static AllocateResourceResult ProjectNotFound() =>
        new(AllocateResourceOutcome.ProjectNotFound, null, "Projecto não encontrado.");

    public static AllocateResourceResult ResourceNotFound() =>
        new(AllocateResourceOutcome.ResourceNotFound, null, "Recurso não encontrado.");

    /// <summary>Campo mal preenchido — 400.</summary>
    public static AllocateResourceResult Rejected(string error) =>
        new(AllocateResourceOutcome.Rejected, null, error);

    /// <summary>Conflito com o estado actual — 409.</summary>
    public static AllocateResourceResult Conflict(string error) =>
        new(AllocateResourceOutcome.Conflict, null, error);
}

public enum AllocateResourceOutcome
{
    Allocated,
    ProjectNotFound,
    ResourceNotFound,
    Rejected,
    Conflict,
}

/// <summary>Termina uma alocação de recurso já existente.</summary>
public sealed class EndResourceAllocation(IProjectStore store, IAuditTrail audit)
{
    public async Task<EndAllocationOutcome> ExecuteAsync(
        Guid projectId, Guid allocationId, DateOnly endsOn, AuditContext context, CancellationToken cancellationToken)
    {
        var projecto = await store.FindForUpdateAsync(projectId, cancellationToken);

        if (projecto is null)
        {
            return EndAllocationOutcome.ProjectNotFound;
        }

        if (projecto.Allocations.All(a => a.Id != allocationId))
        {
            return EndAllocationOutcome.AllocationNotFound;
        }

        try
        {
            projecto.EndResourceAllocation(allocationId, endsOn);
        }
        catch (ArgumentException)
        {
            // Data de fim antes do início da alocação — campo mal
            // preenchido. 400.
            return EndAllocationOutcome.Rejected;
        }
        catch (InvalidOperationException)
        {
            // Alocação já terminada, ou projecto fechado — conflito com o
            // estado actual. 409.
            return EndAllocationOutcome.Conflict;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                ProjectsAuditActions.ResourceAllocationEnded,
                ProjectsAuditEntityTypes.ResourceAllocation,
                allocationId.ToString(),
                context,
                NewValue: $$"""{"endsOn":"{{endsOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return EndAllocationOutcome.Applied;
    }
}

public enum EndAllocationOutcome
{
    Applied,
    ProjectNotFound,
    AllocationNotFound,

    /// <summary>Data de fim antes do início da alocação — campo mal preenchido. 400.</summary>
    Rejected,

    /// <summary>Alocação já terminada, ou projecto fechado — conflito de estado. 409.</summary>
    Conflict,
}
