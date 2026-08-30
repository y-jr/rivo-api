namespace Rivo.Fleet.Domain;

/// <summary>
/// Plano de manutenção preventiva de uma viatura — parte do agregado
/// <see cref="Vehicle"/> (`modules/fleet.md` §Possui).
///
/// <para>
/// <strong>Distinto de <see cref="MaintenanceRecord"/>.</strong> O registo é
/// o que aconteceu; o plano é o calendário — quando a próxima manutenção
/// preventiva é devida. Os dois não se ligam automaticamente: completar um
/// ciclo do plano não exige um registo, e um registo não fecha um plano
/// sozinho. Ligar os dois é trabalho para quando houver um caso de uso real
/// a pedi-lo — não se inventa agora.
/// </para>
///
/// <para>
/// <strong>Ao contrário de Manutenção e Atribuição, vários planos activos ao
/// mesmo tempo são normais</strong> — "óleo a cada 90 dias" e "pneus a cada
/// 180 dias" são dois planos da mesma viatura, sem exclusão mútua.
/// </para>
/// </summary>
public sealed class MaintenancePlan
{
    internal MaintenancePlan(Guid id, Guid vehicleId, string description, int intervalDays, DateOnly nextDueOn)
    {
        Id = id;
        VehicleId = vehicleId;
        Description = description;
        IntervalDays = intervalDays;
        NextDueOn = nextDueOn;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private MaintenancePlan()
    {
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid VehicleId { get; private set; }

    public string Description { get; private set; }

    /// <summary>De quantos em quantos dias o ciclo se repete.</summary>
    public int IntervalDays { get; private set; }

    /// <summary>Data em que o próximo ciclo é devido.</summary>
    public DateOnly NextDueOn { get; private set; }

    /// <summary>
    /// Falso depois de <see cref="Cancel"/> — um plano cancelado nunca mais
    /// conta como devido, mas fica como facto histórico (nunca elimina).
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>Concorrência optimista (ADR-025). O domínio nunca lhe toca.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Verdadeiro quando a data devida já passou, à data indicada — é este o
    /// sinal que sustenta o "alerta": um plano cancelado nunca está atrasado.
    /// </summary>
    public bool IsOverdue(DateOnly asOf) => IsActive && NextDueOn < asOf;

    /// <summary>
    /// Regista que o ciclo actual foi concluído e reagenda o próximo, a
    /// partir de quando foi concluído — não da data que estava marcada, para
    /// não empilhar ciclos em atraso se a conclusão vier tarde.
    /// </summary>
    internal void CompleteCycle(DateOnly completedOn)
    {
        EnsureActivePlan();
        NextDueOn = completedOn.AddDays(IntervalDays);
    }

    internal void Cancel()
    {
        EnsureActivePlan();
        IsActive = false;
    }

    private void EnsureActivePlan()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Este plano de manutenção já está cancelado.");
        }
    }
}
