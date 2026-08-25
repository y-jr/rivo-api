namespace Rivo.Finance.Application;

/// <summary>
/// Configuração de `finance` que depende do ambiente, não do código.
/// </summary>
public sealed class FinanceOptions
{
    public const string SectionName = "Finance";

    /// <summary>
    /// Série de numeração usada quando o pedido não indica nenhuma.
    ///
    /// <para>
    /// **Uma série contínua por tipo de documento, sem reinício anual.** É a
    /// convenção escolhida em 2026-08-25 e está no ADR-036: reiniciar por ano
    /// obrigaria a criar uma série cada Janeiro e a decidir para qual emitir
    /// perto da viragem — mais peças móveis, e cada uma delas uma hipótese de
    /// emitir na série errada. Numeração contínua é igualmente auditável e não
    /// tem essa data.
    /// </para>
    /// </summary>
    public string DefaultSeries { get; init; } = "S001";

    /// <summary>
    /// Cria a série por omissão no arranque, se ainda não existir.
    ///
    /// <para>
    /// Sem isto, um ambiente novo emite `404` na primeira factura até alguém se
    /// lembrar de abrir a série à mão — e o passo esquecido só aparece quando
    /// alguém tenta facturar.
    /// </para>
    /// </summary>
    public bool SeedDefaultSeries { get; init; } = true;

    /// <summary>
    /// Menção impressa na factura enquanto o sistema não estiver certificado
    /// pela AGT.
    ///
    /// <para>
    /// <strong>Vazio desliga-a</strong>, e é assim que se apaga no dia em que
    /// houver <c>SoftwareValidationNumber</c>. As facturas emitidas antes
    /// mantêm a menção que lhes foi congelada — não é derivada em leitura, de
    /// propósito.
    /// </para>
    /// </summary>
    public string FiscalNotice { get; init; } =
        "Documento sem validade fiscal — software não certificado pela AGT.";

    /// <summary>
    /// Identificador a usar no campo de NIF numa venda a consumidor final.
    ///
    /// <para>
    /// <strong>Vazio bloqueia a venda a consumidor final</strong>, com uma
    /// mensagem que diz porquê. A convenção angolana para este identificador
    /// não está verificada em fonte primária, e `CLAUDE.md` proíbe implementar
    /// regras fiscais a partir de levantamento provisório.
    /// </para>
    ///
    /// <para>
    /// O valor por omissão é deliberadamente <em>não</em> um NIF plausível: um
    /// número com ar de real seria tomado por verificado e sobreviveria até à
    /// certificação. <strong>Substituir pelo oficial antes de certificar.</strong>
    /// </para>
    /// </summary>
    public string FinalConsumerTaxId { get; init; } = "CONSUMIDORFINAL";

    /// <summary>Designação que aparece no lugar do nome do cliente.</summary>
    public string FinalConsumerName { get; init; } = "Consumidor final";
}
