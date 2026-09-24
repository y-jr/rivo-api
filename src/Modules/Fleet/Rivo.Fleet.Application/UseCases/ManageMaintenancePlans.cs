using Rivo.Audit.Contracts;
using Rivo.Fleet.Application.Abstractions;
using Rivo.Fleet.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Fleet.Application.UseCases;

/// <summary>Agenda um plano de manutenção preventiva. Vários por viatura são normais.</summary>
public sealed class SchedulePlan(IVehicleStore store, IAuditTrail audit)
{
    public async Task<SchedulePlanResult> ExecuteAsync(
        Guid vehicleId,
        string description,
        int intervalDays,
        DateOnly firstDueOn,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return SchedulePlanResult.NotFound();
        }

        MaintenancePlan plano;

        try
        {
            plano = veiculo.SchedulePlan(description, intervalDays, firstDueOn);
        }
        catch (ArgumentException error)
        {
            return SchedulePlanResult.Rejected(error.Message);
        }
        catch (InvalidOperationException error)
        {
            // Viatura inactiva: conflito com o estado actual, não pedido
            // malformado — 409, não 400.
            return SchedulePlanResult.Conflict(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.PlanScheduled,
                FleetAuditEntityTypes.MaintenancePlan,
                plano.Id.ToString(),
                context,
                NewValue: $$"""{"vehicleId":"{{vehicleId}}","intervalDays":{{plano.IntervalDays}},"nextDueOn":"{{plano.NextDueOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return SchedulePlanResult.Success(plano.Id);
    }
}

public sealed record SchedulePlanResult(SchedulePlanOutcome Outcome, Guid? PlanId, string? Error)
{
    public static SchedulePlanResult Success(Guid planId) => new(SchedulePlanOutcome.Scheduled, planId, null);

    public static SchedulePlanResult NotFound() =>
        new(SchedulePlanOutcome.NotFound, null, "Viatura não encontrada.");

    public static SchedulePlanResult Rejected(string error) => new(SchedulePlanOutcome.Rejected, null, error);

    public static SchedulePlanResult Conflict(string error) => new(SchedulePlanOutcome.Conflict, null, error);
}

public enum SchedulePlanOutcome
{
    Scheduled,
    NotFound,

    /// <summary>Pedido malformado — descrição vazia ou intervalo não positivo. 400.</summary>
    Rejected,

    /// <summary>Viatura inactiva. 409.</summary>
    Conflict,
}

/// <summary>Regista o ciclo actual como concluído e reagenda o próximo.</summary>
public sealed class CompletePlanCycle(IVehicleStore store, IAuditTrail audit)
{
    public async Task<PlanLifecycleOutcome> ExecuteAsync(
        Guid vehicleId, Guid planId, DateOnly completedOn, AuditContext context, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return PlanLifecycleOutcome.VehicleNotFound;
        }

        if (veiculo.Plans.All(p => p.Id != planId))
        {
            return PlanLifecycleOutcome.PlanNotFound;
        }

        try
        {
            veiculo.CompletePlanCycle(planId, completedOn);
        }
        catch (InvalidOperationException)
        {
            return PlanLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        var plano = veiculo.Plans.First(p => p.Id == planId);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.PlanCycleCompleted,
                FleetAuditEntityTypes.MaintenancePlan,
                planId.ToString(),
                context,
                NewValue: $$"""{"completedOn":"{{completedOn:yyyy-MM-dd}}","nextDueOn":"{{plano.NextDueOn:yyyy-MM-dd}}"}"""),
            cancellationToken);

        return PlanLifecycleOutcome.Applied;
    }
}

/// <summary>Cancela um plano. Nunca elimina — fica como facto histórico.</summary>
public sealed class CancelPlan(IVehicleStore store, IAuditTrail audit)
{
    public async Task<PlanLifecycleOutcome> ExecuteAsync(
        Guid vehicleId, Guid planId, AuditContext context, CancellationToken cancellationToken)
    {
        var veiculo = await store.FindForUpdateAsync(vehicleId, cancellationToken);

        if (veiculo is null)
        {
            return PlanLifecycleOutcome.VehicleNotFound;
        }

        if (veiculo.Plans.All(p => p.Id != planId))
        {
            return PlanLifecycleOutcome.PlanNotFound;
        }

        try
        {
            veiculo.CancelPlan(planId);
        }
        catch (InvalidOperationException)
        {
            return PlanLifecycleOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FleetAuditActions.PlanCancelled, FleetAuditEntityTypes.MaintenancePlan, planId.ToString(), context),
            cancellationToken);

        return PlanLifecycleOutcome.Applied;
    }
}

public enum PlanLifecycleOutcome
{
    Applied,
    VehicleNotFound,
    PlanNotFound,

    /// <summary>O plano já estava cancelado. 409.</summary>
    Rejected,
}

/// <summary>
/// A superfície de "alerta" do Plano de Manutenção: viaturas com pelo menos
/// um plano activo devido até <c>withinDays</c> dias a partir de hoje —
/// inclui o já atrasado.
///
/// <para>
/// **Não empurra notificação nenhuma.** `notifications.INotifier` entrega a
/// um `RecipientUserId` de `identity`; não há ainda forma de resolver "todos
/// os `AssetManager`" para um destinatário concreto, e inventar essa
/// resolução aqui seria adivinhar uma peça de `identity` que não existe. O
/// alerta é esta consulta — quem olha para a frota vê o que está por tratar.
/// </para>
/// </summary>
public sealed class ListDueMaintenancePlans(IVehicleStore store, TimeProvider clock)
{
    /// <summary>
    /// Paginação em memória, não na query (excepção a ADR-068): a unidade da
    /// listagem é o plano, mas a `Store` devolve veículos com planos aninhados
    /// — achatar isto num `Skip`/`Take` de SQL exigiria reescrever
    /// <see cref="IVehicleStore.ListWithDuePlansAsync"/> para uma projecção
    /// própria de planos, fora do âmbito desta ronda. O número de veículos com
    /// manutenção a vencer é sempre pequeno (não cresce sem limite como
    /// lançamentos ou movimentos), por isso o corte em memória aqui é seguro.
    /// </summary>
    public async Task<(IReadOnlyList<DueMaintenancePlanView> Items, int? TotalCount)> ExecuteAsync(
        int withinDays, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var veiculos = await store.ListWithDuePlansAsync(hoje, withinDays, cancellationToken);

        var planos = veiculos.SelectMany(v => v.Plans
            .Where(p => p.IsActive && p.NextDueOn <= hoje.AddDays(withinDays))
            .Select(p => new DueMaintenancePlanView(
                v.Id, v.PlateNumber, p.Id, p.Description, p.NextDueOn, p.IsOverdue(hoje))))
            .OrderBy(p => p.NextDueOn)
            .ToList();

        if (pagina is not { } p2)
        {
            return (planos, null);
        }

        var fatia = planos.Skip((p2.Page - 1) * p2.PageSize).Take(p2.PageSize).ToList();
        return (fatia, planos.Count);
    }
}

public sealed record DueMaintenancePlanView(
    Guid VehicleId, string PlateNumber, Guid PlanId, string Description, DateOnly NextDueOn, bool IsOverdue);
