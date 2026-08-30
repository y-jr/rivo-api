namespace Rivo.Fiscal.Domain;

/// <summary>
/// Vocabulário interno de `fiscal`.
///
/// <para>
/// <strong>Duplica deliberadamente o de `Rivo.Fiscal.Contracts`.</strong> Não é
/// descuido nem dívida: o domínio não referencia os contratos, e é a camada
/// Application que traduz entre os dois. É o mesmo padrão que `hr` usa em
/// <c>EmployeeStatus</c>, e existe pela razão do ADR-010 — o modelo interno tem
/// de poder evoluir sem que cada alteração seja uma quebra de contrato para
/// quem consome.
/// </para>
/// </summary>
public enum TaxKind
{
    ValueAdded,

    /// <summary>
    /// A parcela do trabalhador (3%, Decreto Presidencial n.º 227/18) — a
    /// única dedutível à matéria colectável do IRT (artigo 7.º do CIRT).
    /// </summary>
    EmployeeSocialSecurity,

    /// <summary>
    /// A parcela patronal (8%) — custo da empresa, nunca dedutível ao
    /// rendimento do trabalhador. `payroll` ainda não a consome: não há campo
    /// no modelo de dados para o custo da entidade patronal.
    /// </summary>
    EmployerSocialSecurity,
}

/// <summary>
/// Códigos do SAF-T AO com significado fixado em documento.
///
/// <para>
/// Só estes dois. Vêm de `modules/commercial.md`, que os cita da DS.120 v1.4:
/// são os que obrigam a <c>TaxExemptionCode</c>. Os restantes códigos da tabela
/// não estão verificados em fonte primária, e por isso são texto que quem
/// introduz os dados fornece — não constantes que o domínio finge conhecer.
/// </para>
/// </summary>
public static class TaxCodes
{
    /// <summary>Isento.</summary>
    public const string Exempt = "ISE";

    /// <summary>Não sujeito.</summary>
    public const string NotSubject = "NS";

    /// <summary>
    /// Código interno para as séries de INSS (<see cref="TaxKind.EmployeeSocialSecurity"/>,
    /// <see cref="TaxKind.EmployerSocialSecurity"/>). Não vem do SAF-T — o
    /// INSS não tem código de imposto na tabela do SAF-T do jeito que o IVA
    /// tem; existe só para reaproveitar `TaxRateSchedule`, que exige um.
    /// </summary>
    public const string SocialSecurity = "INSS";

    /// <summary>
    /// <c>taxCode ∈ { ISE, NS } → taxExemptionCode obrigatório</c>.
    ///
    /// <para>
    /// É regra de domínio e não validação de interface: uma linha com estes
    /// códigos e sem código de isenção válido não pode ser emitida
    /// (`modules/commercial.md`).
    /// </para>
    /// </summary>
    public static bool RequiresExemptionCode(string taxCode) =>
        string.Equals(taxCode, Exempt, StringComparison.OrdinalIgnoreCase)
        || string.Equals(taxCode, NotSubject, StringComparison.OrdinalIgnoreCase);
}
