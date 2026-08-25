using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Persistência do Planeamento: centros de custo, orçamentos e previsões.
/// </summary>
public interface IPlanningStore
{
    Task<CostCentre?> FindCostCentreAsync(Guid costCentreId, CancellationToken cancellationToken);

    Task<CostCentre?> FindCostCentreForUpdateAsync(Guid costCentreId, CancellationToken cancellationToken);

    Task<bool> CostCentreCodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Os centros de custo mapeados a um departamento.
    ///
    /// <para>
    /// <strong>Lista, não um só</strong>: o mapeamento é opcional e não é 1:1
    /// (D4). Um departamento pode alimentar vários centros de custo, e é quem
    /// chama que decide o que fazer com isso — em BR-8, a ambiguidade é motivo
    /// para recusar, não para escolher um.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CostCentre>> ListCostCentresForDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CostCentre>> ListCostCentresAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task AddCostCentreAsync(CostCentre costCentre, CancellationToken cancellationToken);

    Task<Budget?> FindBudgetAsync(Guid budgetId, CancellationToken cancellationToken);

    Task<Budget?> FindBudgetForUpdateAsync(Guid budgetId, CancellationToken cancellationToken);

    /// <summary>
    /// O orçamento de um centro de custo num ano. Um só por par — dois tectos
    /// para o mesmo ano tornariam a verificação de BR-8 ambígua.
    /// </summary>
    Task<Budget?> FindBudgetForAsync(
        Guid costCentreId,
        int fiscalYear,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Budget>> ListBudgetsAsync(
        Guid? costCentreId,
        int? fiscalYear,
        CancellationToken cancellationToken);

    Task AddBudgetAsync(Budget budget, CancellationToken cancellationToken);

    /// <summary>
    /// Quanto já está comprometido num centro de custo num mês.
    ///
    /// <para>
    /// <strong>Compromissos, não realizações.</strong> Conta os pedidos de
    /// pagamento não cancelados — pendentes e executados — imputados àquele
    /// centro de custo, pela data do pedido. Um pedido em curso já promete o
    /// dinheiro, e esperar pelo lançamento contabilístico deixaria passar tudo
    /// até ao fecho do mês.
    /// </para>
    ///
    /// <para>
    /// É a mesma leitura que <see cref="IPayablesStore.CommittedAsync"/> faz
    /// sobre uma factura de compra, e pela mesma razão.
    /// </para>
    ///
    /// <para>
    /// ⚠ <strong>O que fica de fora:</strong> despesa que chegue aos livros sem
    /// passar por um pedido de pagamento — um lançamento directo, um acréscimo
    /// de salários — não consome orçamento. Enquanto nem tudo o que gasta
    /// passar por aqui, este número é um limite inferior do consumo real.
    /// </para>
    /// </summary>
    Task<decimal> CommittedAgainstAsync(
        Guid costCentreId,
        int fiscalYear,
        int month,
        CancellationToken cancellationToken);

    Task<DepartmentCostForecast?> FindForecastAsync(Guid forecastId, CancellationToken cancellationToken);

    Task<DepartmentCostForecast?> FindForecastForUpdateAsync(Guid forecastId, CancellationToken cancellationToken);

    Task<bool> ForecastExistsAsync(
        Guid departmentId,
        int fiscalYear,
        int month,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DepartmentCostForecast>> ListForecastsAsync(
        Guid? departmentId,
        int? fiscalYear,
        CancellationToken cancellationToken);

    Task AddForecastAsync(DepartmentCostForecast forecast, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
