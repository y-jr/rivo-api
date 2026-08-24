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
/// Impostos que `fiscal` determina.
///
/// <para>
/// Só o IVA, por ora. O IRT e o INSS precisam de regras que as fontes
/// secundárias contradizem — escalões, dedutibilidade, tecto contributivo — e
/// que `CLAUDE.md` proíbe implementar sem verificação profissional. Acrescentar
/// aqui um valor que ninguém sabe calcular seria pior do que a ausência.
/// </para>
/// </summary>
public enum TaxKind
{
    ValueAdded,
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
