namespace Rivo.Fiscal.Contracts;

/// <summary>
/// Superfície publicada de `fiscal`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>Âmbito reduzido por ADR-036.</strong> Emite-se com a forma do
/// documento fiscal e sem conformidade legal, portanto daqui só sai a
/// determinação de imposto. Exportação SAF-T, declarações periódicas e o motor
/// de IRT/INSS ficam adiados — e as regras que precisariam continuam por
/// verificar profissionalmente.
/// </para>
/// </summary>
public interface ITaxDetermination
{
    /// <summary>
    /// Determina o imposto aplicável a uma operação.
    ///
    /// <para>
    /// <strong>À data do facto gerador, nunca à data do cálculo</strong>
    /// (ADR-011 §3). Uma correcção emitida em 2027 sobre um facto de 2026
    /// aplica as regras de 2026 — e é por isso que a data é parâmetro
    /// obrigatório em vez de ser <c>DateTime.UtcNow</c> lá dentro.
    /// </para>
    /// </summary>
    Task<TaxDeterminationResult> DetermineAsync(
        TaxDeterminationRequest request,
        CancellationToken cancellationToken);
}

/// <param name="TaxPointDate">
/// Data do facto gerador. Não é a data de emissão do documento, embora
/// coincidam no caso corrente.
/// </param>
public sealed record TaxDeterminationRequest(TaxKind Kind, string TaxCode, DateOnly TaxPointDate);

/// <param name="Determination">Preenchido apenas quando houve determinação.</param>
public sealed record TaxDeterminationResult(TaxDeterminationOutcome Outcome, TaxDetermination? Determination)
{
    public static TaxDeterminationResult Determined(TaxDetermination determination) =>
        new(TaxDeterminationOutcome.Determined, determination);

    public static TaxDeterminationResult NoRateInForce() =>
        new(TaxDeterminationOutcome.NoRateInForce, null);

    public static TaxDeterminationResult ExemptionCodeUnavailable() =>
        new(TaxDeterminationOutcome.ExemptionCodeUnavailable, null);
}

public enum TaxDeterminationOutcome
{
    Determined,

    /// <summary>
    /// Não há taxa em vigor para este código à data pedida.
    ///
    /// <para>
    /// <strong>Recusa, não omissão.</strong> Aplicar "a taxa actual" a um facto
    /// passado sem cobertura seria inventar o valor — e o erro só apareceria na
    /// conferência, meses depois.
    /// </para>
    /// </summary>
    NoRateInForce,

    /// <summary>
    /// O código é de isenção (ISE) ou de não sujeição (NS), que exigem
    /// <c>TaxExemptionCode</c> — e o catálogo desses códigos não existe.
    ///
    /// <para>
    /// O ADR-036 adiou-o, e `modules/commercial.md` é explícito quanto ao que
    /// **não** se faz entretanto: não se inventa código. A emissão com isenção
    /// fica bloqueada até haver a lista oficial.
    /// </para>
    /// </summary>
    ExemptionCodeUnavailable,
}

/// <param name="LegalInstrument">
/// O diploma que fixou esta taxa (ADR-011 §4). Viaja com a determinação para
/// que a auditoria possa responder a "porquê este valor".
/// </param>
public sealed record TaxDetermination(
    string TaxCode,
    decimal Percentage,
    string LegalInstrument);

/// <summary>
/// Determina o IRT devido sobre uma matéria colectável, por tabela de
/// escalões progressivos.
///
/// <para>
/// <strong>Devolve o imposto já calculado</strong> — ao contrário de
/// <see cref="ITaxDetermination"/>, que devolve a percentagem e deixa quem
/// pergunta multiplicar. A diferença é deliberada: "percentagem × montante"
/// não é uma regra fiscal, é aritmética que qualquer módulo pode fazer
/// correctamente; "Parcela Fixa + Taxa × (Matéria Colectável − Excesso de)"
/// é exactamente o tipo de regra que `modules/fiscal.md` reserva a este
/// módulo — "nenhum outro módulo pode implementar regras de imposto por sua
/// conta".
/// </para>
/// </summary>
public interface IIncomeTaxDetermination
{
    /// <summary>
    /// Determina o IRT devido sobre <paramref name="request"/>.TaxableIncome,
    /// à data do facto gerador — nunca à data do cálculo (ADR-011 §3), pela
    /// mesma razão de <see cref="ITaxDetermination.DetermineAsync"/>.
    /// </summary>
    Task<IncomeTaxDeterminationResult> DetermineAsync(
        IncomeTaxDeterminationRequest request,
        CancellationToken cancellationToken);
}

/// <param name="TaxableIncome">
/// A matéria colectável — já com o INSS do trabalhador e as componentes
/// isentas deduzidas. Este contrato não sabe nada do salário bruto.
/// </param>
/// <param name="TaxPointDate">Data do facto gerador.</param>
public sealed record IncomeTaxDeterminationRequest(decimal TaxableIncome, DateOnly TaxPointDate);

public sealed record IncomeTaxDeterminationResult(
    IncomeTaxDeterminationOutcome Outcome, IncomeTaxDetermination? Determination)
{
    public static IncomeTaxDeterminationResult Determined(IncomeTaxDetermination determination) =>
        new(IncomeTaxDeterminationOutcome.Determined, determination);

    public static IncomeTaxDeterminationResult NoScheduleInForce() =>
        new(IncomeTaxDeterminationOutcome.NoScheduleInForce, null);
}

public enum IncomeTaxDeterminationOutcome
{
    Determined,

    /// <summary>
    /// Não há tabela de escalões em vigor à data pedida. Recusa, não omissão
    /// — mesma razão de <see cref="TaxDeterminationOutcome.NoRateInForce"/>.
    /// </summary>
    NoScheduleInForce,
}

/// <param name="Amount">O IRT devido, já calculado.</param>
/// <param name="Rate">A taxa marginal do escalão aplicado.</param>
/// <param name="FixedPortion">A parcela fixa do escalão aplicado.</param>
/// <param name="BracketLowerBound">
/// O "excesso de" do escalão aplicado — o limiar a partir do qual a taxa
/// marginal incide.
/// </param>
/// <param name="LegalInstrument">
/// O diploma que fixou esta tabela (ADR-011 §4).
/// </param>
public sealed record IncomeTaxDetermination(
    decimal Amount,
    decimal Rate,
    decimal FixedPortion,
    decimal BracketLowerBound,
    string LegalInstrument);

/// <summary>
/// Determina o limiar de isenção de IRT de um subsídio — a dedução
/// "componentes não sujeitas/isentas" do artigo 7.º do CIRT, que o cálculo
/// do IRT aplica depois do INSS (`modules/fiscal.md` §"Matéria colectável").
///
/// <para>
/// <strong>Só Alimentação e Transporte têm limiar</strong>
/// (<see cref="SubsidyKind"/>) — Férias e Natal são tributados normalmente,
/// sem excepção, confirmado pelo utilizador (`state/pending-decisions.md`).
/// Devolve um <strong>montante</strong>, não uma taxa: quem pergunta aplica
/// <c>Math.Min(valorDoSubsídio, limiar)</c> — essa aritmética não é regra
/// fiscal, o limiar em si é.
/// </para>
/// </summary>
public interface ISubsidyExemptionDetermination
{
    Task<SubsidyExemptionResult> DetermineAsync(
        SubsidyExemptionRequest request,
        CancellationToken cancellationToken);
}

/// <param name="TaxPointDate">Data do facto gerador — o fim do período da folha.</param>
public sealed record SubsidyExemptionRequest(SubsidyKind Kind, DateOnly TaxPointDate);

public sealed record SubsidyExemptionResult(SubsidyExemptionOutcome Outcome, SubsidyExemption? Exemption)
{
    public static SubsidyExemptionResult Determined(SubsidyExemption exemption) =>
        new(SubsidyExemptionOutcome.Determined, exemption);

    public static SubsidyExemptionResult NoThresholdInForce() =>
        new(SubsidyExemptionOutcome.NoThresholdInForce, null);
}

public enum SubsidyExemptionOutcome
{
    Determined,

    /// <summary>
    /// Não há limiar em vigor à data pedida. Recusa, não omissão — mesma
    /// razão de <see cref="TaxDeterminationOutcome.NoRateInForce"/>.
    /// </summary>
    NoThresholdInForce,
}

/// <param name="Amount">O "isento até", em Kwanzas por mês.</param>
/// <param name="LegalInstrument">
/// A fonte que fixou este limiar (ADR-011 §4) — aqui, tipicamente, a data em
/// que o utilizador o confirmou directamente, não um diploma: ver a reserva
/// em `state/pending-decisions.md`.
/// </param>
public sealed record SubsidyExemption(decimal Amount, string LegalInstrument);

/// <summary>
/// Os subsídios com limiar de isenção próprio no IRT. Ver
/// <see cref="ISubsidyExemptionDetermination"/>.
/// </summary>
public enum SubsidyKind
{
    FoodAllowance,
    TransportAllowance,
}

/// <summary>
/// Impostos que `fiscal` determina.
///
/// <para>
/// O IVA usa <see cref="ITaxDetermination"/> — uma taxa plana com vigência.
/// O INSS reaproveita o mesmo mecanismo (também é plano). O IRT tem tabela
/// própria de escalões progressivos, publicada por
/// <see cref="IIncomeTaxDetermination"/>.
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
    /// rendimento do trabalhador. `payroll` ainda não a consome.
    /// </summary>
    EmployerSocialSecurity,
}

/// <summary>
/// Códigos de imposto do SAF-T AO com significado fixado em documento.
///
/// <para>
/// <strong>Só estes dois, e de propósito.</strong> Vêm de
/// `modules/commercial.md`, que os cita da DS.120 v1.4: são os que obrigam a
/// <c>TaxExemptionCode</c>. Os restantes códigos da tabela não estão
/// verificados em fonte primária, e por isso são texto que quem introduz os
/// dados fornece — não constantes que o código finge conhecer.
/// </para>
/// </summary>
public static class TaxCodes
{
    /// <summary>Isento.</summary>
    public const string Exempt = "ISE";

    /// <summary>Não sujeito.</summary>
    public const string NotSubject = "NS";

    /// <summary>
    /// Código interno para as séries de INSS. Não vem do SAF-T — existe só
    /// para reaproveitar <see cref="ITaxDetermination"/>, que exige um código
    /// por série.
    /// </summary>
    public const string SocialSecurity = "INSS";

    /// <summary>
    /// <c>taxCode ∈ { ISE, NS } → taxExemptionCode obrigatório</c>, da
    /// DS.120 v1.4 (`modules/commercial.md`).
    /// </summary>
    public static bool RequiresExemptionCode(string taxCode) =>
        string.Equals(taxCode, Exempt, StringComparison.OrdinalIgnoreCase)
        || string.Equals(taxCode, NotSubject, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Catálogo de permissões de `fiscal`, declarado pelo próprio módulo.</summary>
public static class FiscalPermissions
{
    public const string RatesRead = "fiscal.rates.read";

    /// <summary>
    /// Introduzir versões de taxa.
    ///
    /// <para>
    /// <strong>Apenas Admin.</strong> O ADR-011 §6 exige acesso restrito e
    /// auditado: quem controla a taxa controla o valor de todas as facturas
    /// emitidas a partir da data que escolher.
    /// </para>
    /// </summary>
    public const string RatesWrite = "fiscal.rates.write";

    public static readonly IReadOnlyList<string> All = [RatesRead, RatesWrite];
}
