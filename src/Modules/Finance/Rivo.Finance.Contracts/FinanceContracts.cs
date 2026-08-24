namespace Rivo.Finance.Contracts;

/// <summary>
/// Superfície publicada de `finance`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>Âmbito reduzido por ADR-036.</strong> Daqui só sai a factura de
/// venda. Contas a Pagar, Tesouraria, Contabilidade e Planeamento continuam por
/// fazer — e com eles o <em>disponível orçamental</em> que BR-8 exige de
/// `approval`, e a execução de pagamento de BR-1 e BR-5.
/// </para>
/// </summary>
public static class FinancePermissions
{
    public const string InvoicesRead = "finance.invoices.read";

    public const string InvoicesWrite = "finance.invoices.write";

    /// <summary>
    /// Anular uma factura emitida.
    ///
    /// <para>
    /// <strong>Separada de <see cref="InvoicesWrite"/> de propósito.</strong>
    /// Emitir e desfazer não são a mesma autorização: a anulação é a única
    /// alteração possível a um documento fiscal, e quem a pode fazer devia ser
    /// decidido à parte de quem factura no dia-a-dia.
    /// </para>
    /// </summary>
    public const string InvoicesCancel = "finance.invoices.cancel";

    /// <summary>
    /// Gerir séries de numeração.
    ///
    /// <para>
    /// <strong>Apenas Admin.</strong> Abrir uma série paralela é a forma óbvia
    /// de emitir fora da sequência auditável.
    /// </para>
    /// </summary>
    public const string SeriesWrite = "finance.series.write";

    public static readonly IReadOnlyList<string> All =
        [InvoicesRead, InvoicesWrite, InvoicesCancel, SeriesWrite];

    /// <summary>
    /// O que um perfil de facturação recebe: emitir e consultar, sem anular e
    /// sem abrir séries.
    /// </summary>
    public static readonly IReadOnlyList<string> ForBilling = [InvoicesRead, InvoicesWrite];
}
