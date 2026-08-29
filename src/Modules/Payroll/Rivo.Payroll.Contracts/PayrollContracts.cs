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
