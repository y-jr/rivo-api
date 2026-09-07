namespace Rivo.Payroll.Contracts;

/// <summary>
/// Superfície publicada de `payroll`. Assembly sem dependências (ADR-017).
///
/// <para>
/// O catálogo de permissões e <see cref="IPayrollSelfService"/>, que o Portal
/// do Colaborador consome desde 2026-09-07. `finance` (custo salarial) e
/// `fiscal` (base de IRT/INSS) continuam a ser consumidores previstos em
/// `modules/payroll.md` e sem contrato publicado.
/// </para>
/// </summary>
public static class PayrollPermissions
{
    public const string RunsRead = "payroll.runs.read";
    public const string RunsWrite = "payroll.runs.write";

    public static readonly IReadOnlyList<string> All = [RunsRead, RunsWrite];
}

/// <summary>
/// O que um colaborador pode ver sobre a **sua própria** remuneração — os
/// recibos, um por período (ADR-042).
///
/// <para>
/// Recebe sempre um <c>employeeId</c> já resolvido, e nunca a conta: resolver
/// "o próprio" é da camada de composição. Mesma disciplina do
/// <c>IEmployeeSelfService</c> de `hr`.
/// </para>
///
/// <para>
/// ⚠ <strong>Só devolve itens de folhas aprovadas.</strong> Um item de uma
/// folha em rascunho ou pendente de decisão é um número por confirmar, e
/// mostrá-lo ao colaborador seria prometer-lhe um vencimento que ainda pode
/// mudar — ou que pode nunca ser aprovado. O filtro vive aqui e não no
/// consumidor, para nenhum consumidor futuro o poder esquecer.
/// </para>
/// </summary>
public interface IPayrollSelfService
{
    Task<IReadOnlyList<OwnPayslip>> ListPayslipsAsync(
        Guid employeeId,
        CancellationToken cancellationToken);
}

/// <param name="NetSalary">
/// Nulo enquanto o item não tiver sido calculado. Não é zero — é "ainda não
/// se sabe", e o ecrã tem de os distinguir.
/// </param>
/// <param name="DocumentId">
/// O recibo em PDF, quando existe. Nulo é "ainda não foi emitido", e não
/// impede o item de existir: o cálculo e o documento são actos separados.
/// </param>
public sealed record OwnPayslip(
    Guid ItemId,
    Guid RunId,
    int Year,
    int Month,
    decimal GrossSalary,
    decimal FoodAllowance,
    decimal TransportAllowance,
    decimal VacationAllowance,
    decimal ChristmasAllowance,
    decimal? NetSalary,
    decimal? WithholdingTax,
    decimal? SocialSecurityContribution,
    Guid? DocumentId);
