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

    /// <summary>Consultar recibos e o que ficou por receber.</summary>
    public const string ReceiptsRead = "finance.receipts.read";

    /// <summary>
    /// Registar dinheiro recebido.
    ///
    /// <para>
    /// <strong>Separada de <see cref="InvoicesWrite"/>.</strong> Emitir uma
    /// factura é dizer o que é devido; registar um recibo é dizer que entrou
    /// dinheiro. Quem pode declarar recebimentos sem cobrar nada pode fazer uma
    /// dívida desaparecer — é a razão de a cobrança e a tesouraria serem
    /// funções distintas.
    /// </para>
    ///
    /// <para>
    /// <strong>Estornar não vem com esta:</strong> exige
    /// <see cref="InvoicesCancel"/>, porque desfazer um recebimento faz a dívida
    /// voltar a existir.
    /// </para>
    /// </summary>
    public const string ReceiptsWrite = "finance.receipts.write";

    /// <summary>Consultar contas bancárias, facturas de compra e pedidos de pagamento.</summary>
    public const string PayablesRead = "finance.payables.read";

    /// <summary>
    /// Registar facturas de compra, abrir contas e carregar fundos.
    /// </summary>
    public const string PayablesWrite = "finance.payables.write";

    /// <summary>
    /// Pedir um pagamento. Não o executa — submete-o a governança.
    ///
    /// <para>
    /// Separada de <see cref="PaymentsExecute"/> porque são funções distintas:
    /// quem pede não deve poder pagar sozinho o que pediu.
    /// </para>
    /// </summary>
    public const string PaymentsRequest = "finance.payments.request";

    /// <summary>
    /// Executar um pagamento — tirar dinheiro da conta.
    ///
    /// <para>
    /// <strong>A permissão abre a porta; as regras é que decidem.</strong> BR-1
    /// exige decisão aprovada, BR-5 revalida-a no momento e verifica o saldo, e
    /// BR-3 recusa se quem paga foi quem aprovou. Ter esta permissão não
    /// dispensa nenhuma das três.
    /// </para>
    /// </summary>
    public const string PaymentsExecute = "finance.payments.execute";

    public static readonly IReadOnlyList<string> All =
    [
        InvoicesRead, InvoicesWrite, InvoicesCancel, SeriesWrite,
        ReceiptsRead, ReceiptsWrite,
        PayablesRead, PayablesWrite, PaymentsRequest, PaymentsExecute,
    ];

    /// <summary>
    /// O que um perfil de facturação recebe: emitir e consultar — incluindo os
    /// recibos, para saber o que está pago — **sem** registar recebimentos,
    /// sem creditar e sem anular.
    /// </summary>
    public static readonly IReadOnlyList<string> ForBilling =
        [InvoicesRead, InvoicesWrite, ReceiptsRead];

    /// <summary>
    /// O que um perfil de tesouraria recebe: ver o que é devido, registar o que
    /// entrou, e **executar** o que já foi aprovado.
    ///
    /// <para>
    /// <strong>Sem `PaymentsRequest`, e é BR-3 na forma do catálogo.</strong>
    /// Quem executa não pede: se pedisse e pagasse, faltava só aprovar — e a
    /// aprovação está em `approval`, que recusa quem submeteu (BR-2). As três
    /// funções são de três pessoas.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ForTreasury =
        [InvoicesRead, ReceiptsRead, ReceiptsWrite, PayablesRead, PaymentsExecute];

    /// <summary>
    /// O que um perfil que compra recebe: registar facturas de fornecedor e
    /// pedir que sejam pagas. **Não paga.**
    /// </summary>
    public static readonly IReadOnlyList<string> ForPayables =
        [PayablesRead, PayablesWrite, PaymentsRequest];
}
