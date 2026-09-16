namespace Rivo.Payroll.Contracts;

/// <summary>
/// Superfície publicada de `payroll`. Assembly sem dependências (ADR-017).
///
/// <para>
/// Só o catálogo de permissões, por agora — sem consumidor ainda para um
/// contrato de leitura. `finance` (custo salarial) e `fiscal` (base de
/// IRT/INSS) são os consumidores previstos em `modules/payroll.md`, mas não
/// há cálculo nenhum para publicar enquanto o motor fiscal não existir.
/// </para>
/// </summary>
public static class PayrollPermissions
{
    public const string RunsRead = "payroll.runs.read";
    public const string RunsWrite = "payroll.runs.write";

    public static readonly IReadOnlyList<string> All = [RunsRead, RunsWrite];
}

/// <summary>
/// Os recibos que o Portal do Colaborador mostra ao próprio (ADR-062).
///
/// <para>
/// <strong>Só de folhas aprovadas, e o filtro vive aqui.</strong> Um item de
/// folha em rascunho é um número por confirmar; mostrá-lo prometeria um
/// vencimento que ainda pode mudar — e retirá-lo depois seria pior do que
/// nunca o ter mostrado. Deixar o filtro ao consumidor tornaria isto uma regra
/// que cada chamador teria de se lembrar de aplicar.
/// </para>
///
/// <para>
/// Recebe o <c>employeeId</c> já resolvido pela composição a partir da conta
/// autenticada. Este contrato não sabe o que é uma sessão, e por isso não há
/// por onde pedir o recibo de outra pessoa.
/// </para>
/// </summary>
public interface IPayrollSelfService
{
    Task<IReadOnlyList<OwnPayslip>> ListPayslipsAsync(
        Guid employeeId,
        CancellationToken cancellationToken);
}

/// <param name="NetSalary">
/// Nulo é «ainda não calculado», e não zero — o cálculo fiscal é acto separado
/// de criar o item.
/// </param>
/// <param name="DocumentId">
/// Nulo é «recibo ainda não emitido». O cálculo e o documento são actos
/// distintos, e um item pode existir sem o outro.
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
