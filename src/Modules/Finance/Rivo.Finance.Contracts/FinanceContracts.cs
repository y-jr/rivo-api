namespace Rivo.Finance.Contracts;

/// <summary>
/// Disponível orçamental — <strong>o contrato que BR-8 exige</strong>, e um dos
/// dois pontos onde o `docs` avisa que o God Module pode nascer.
///
/// <para>
/// Por isso é <strong>deliberadamente estreito</strong>: uma pergunta e uma
/// resposta. `approval` não vê orçamentos, nem centros de custo, nem
/// lançamentos — pergunta se um valor cabe, e recebe se cabe. Publicar aqui o
/// orçamento inteiro seria dar a `approval` uma vista sobre `finance` que
/// nenhuma regra dele precisa.
/// </para>
/// </summary>
public interface IBudgetAvailability
{
    Task<BudgetCheckResult> CheckAsync(BudgetCheck check, CancellationToken cancellationToken);
}

/// <param name="Reference">
/// A rubrica exacta, quando quem submeteu a conhece — o identificador de um
/// centro de custo, em texto. <strong>Atravessa `approval` sem ser
/// interpretado</strong>, como o `SourceReference` já faz.
/// </param>
/// <param name="DepartmentId">
/// O recuo, para quem não conhece rubricas. `finance` traduz departamento →
/// centro de custo, e <strong>recusa se a tradução for ambígua</strong>: o
/// mapeamento não é 1:1 (D4), e escolher um centro ao acaso seria verificar
/// contra um tecto que ninguém indicou.
/// </param>
/// <param name="On">
/// A data por que se verifica. O orçamento é mensal, e é este dia que escolhe o
/// mês — passá-lo como parâmetro em vez de ler o relógio lá dentro é a mesma
/// razão de ADR-011 §3: a resposta tem de ser reprodutível.
/// </param>
public sealed record BudgetCheck(
    string? Reference,
    Guid? DepartmentId,
    decimal Amount,
    string Currency,
    DateOnly On);

/// <param name="Ceiling">O tecto do mês. Nulo quando não há orçamento que o diga.</param>
/// <param name="Committed">
/// O que já está comprometido nesse mês. <strong>Compromissos, não
/// realizações:</strong> um pedido de pagamento em curso já promete o dinheiro,
/// e esperar pelo lançamento contabilístico deixaria passar tudo até ao fecho.
/// </param>
public sealed record BudgetCheckResult(
    BudgetCheckOutcome Outcome,
    decimal? Ceiling,
    decimal? Committed,
    decimal? Available,
    string? Reason)
{
    public static BudgetCheckResult Within(decimal ceiling, decimal committed, decimal available) =>
        new(BudgetCheckOutcome.Within, ceiling, committed, available, null);

    public static BudgetCheckResult Exceeded(
        decimal ceiling, decimal committed, decimal available, string reason) =>
        new(BudgetCheckOutcome.Exceeded, ceiling, committed, available, reason);

    public static BudgetCheckResult Unverifiable(BudgetCheckOutcome outcome, string reason) =>
        new(outcome, null, null, null, reason);
}

/// <summary>
/// <strong>Nenhum destes resultados aprova por omissão.</strong> Quatro dos
/// cinco são recusa, e é assim de propósito: uma política que exige verificação
/// orçamental está a dizer que não se decide sem saber. "Não consegui
/// verificar" não é "pode avançar".
/// </summary>
public enum BudgetCheckOutcome
{
    /// <summary>Cabe. É o único que deixa avançar.</summary>
    Within,

    /// <summary>Há orçamento e o valor não cabe.</summary>
    Exceeded,

    /// <summary>
    /// Não há orçamento aprovado para aquele centro de custo naquele mês — ou
    /// há só um rascunho, que não controla nada.
    /// </summary>
    NoBudget,

    /// <summary>
    /// O departamento não tem centro de custo associado, ou o processo nem
    /// departamento traz. Sem isso não há orçamento contra que verificar — e o
    /// mapeamento é opcional por desenho (D4), logo isto é um estado normal e
    /// não um defeito.
    /// </summary>
    NoCostCentre,

    /// <summary>
    /// O pedido é numa moeda e o orçamento noutra. <strong>Não se converte</strong>
    /// — o câmbio é uma decisão, e ninguém a tomou aqui. Mesma posição que a
    /// execução de pagamento toma.
    /// </summary>
    CurrencyMismatch,
}

/// <summary>
/// Leitura agregada de AR (Contas a Receber) — receita facturada, saldo em
/// aberto, e os clientes que mais facturaram. Primeiro passo do Dashboard
/// Executivo (Fase 8): sem isto, nada compõe.
///
/// <para>
/// <strong>Moeda sempre explícita, nunca somada entre moedas</strong> —
/// mesma disciplina de <see cref="BudgetCheck"/>. Um total em AOA e um em
/// USD não são um número: são dois. Quem chama pergunta por uma de cada
/// vez; o consumidor decide como as mostra lado a lado.
/// </para>
///
/// <para>
/// <strong>Só o corrente, nunca um saldo a uma data passada.</strong> Uma
/// factura vencida há um mês está tão em aberto hoje como estava — o que
/// varia é se há factura nova ou recebimento novo desde então. Reconstruir
/// "quanto se devia no dia X" exigiria somar todos os movimentos até essa
/// data, um problema maior sem consumidor real a pedi-lo (mesma fronteira
/// que `GET /inventory/valuation` já traça em `modules/inventory.md`).
/// </para>
/// </summary>
public interface IReceivablesOverview
{
    /// <summary>
    /// Receita facturada no período: soma do valor líquido (sem imposto —
    /// imposto cobrado é passivo perante o Estado, não receita) das
    /// facturas de venda emitidas no período, menos o das notas de crédito
    /// emitidas no período — ambos <strong>não anulados</strong>. Uma nota
    /// de crédito reduz a receita do período em que é emitida, não do
    /// período da factura original.
    /// </summary>
    Task<decimal> GetNetRevenueAsync(DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// O que falta receber, agora, de todas as facturas não anuladas nesta
    /// moeda — a mesma conta de <c>GetInvoiceBalance</c> (Application),
    /// somada sobre o conjunto em vez de por factura.
    /// </summary>
    Task<decimal> GetOutstandingReceivablesAsync(string currency, CancellationToken cancellationToken);

    /// <summary>
    /// Os clientes que mais facturaram no período, por valor líquido — só
    /// os com <c>CustomerId</c> real. Consumidor final fica de fora: são
    /// vendas anónimas de balcão, não uma relação com um cliente para
    /// ranquear.
    /// </summary>
    Task<IReadOnlyList<CustomerRevenueView>> GetTopCustomersAsync(
        DateOnly from, DateOnly to, string currency, int count, CancellationToken cancellationToken);

    /// <summary>
    /// A mesma conta de <see cref="GetNetRevenueAsync"/>, restrita a um
    /// cliente. Primeiro consumidor: o Portal do Cliente (ADR-043).
    /// </summary>
    Task<decimal> GetCustomerNetRevenueAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>A mesma conta de <see cref="GetOutstandingReceivablesAsync"/>, restrita a um cliente.</summary>
    Task<decimal> GetCustomerOutstandingAsync(
        Guid customerId, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// As facturas de venda de um cliente — o que o Portal do Cliente mostra
    /// como "as minhas facturas". Sem filtro de período: é o histórico
    /// completo do cliente, não um recorte.
    /// </summary>
    Task<IReadOnlyList<CustomerInvoiceView>> ListCustomerInvoicesAsync(
        Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Extracto de conta corrente de um cliente: saldo de abertura (o que
    /// já se devia, calculado à mesma conta de
    /// <see cref="GetCustomerOutstandingAsync"/>, só que cortada antes de
    /// <paramref name="from"/>), os movimentos do período — facturas a
    /// débito, notas de crédito e recibos a crédito — e o saldo de fecho.
    ///
    /// <para>
    /// <strong>Não é a mesma coisa que reconstruir um saldo a uma data
    /// passada arbitrária</strong> (fronteira que
    /// <see cref="GetOutstandingReceivablesAsync"/> já traça): a abertura
    /// aqui é uma soma directa sobre documentos com data anterior, não uma
    /// cadeia de estado calculado a percorrer — o mesmo tipo de conta que
    /// já se faz para "o que está em aberto agora", só com o corte de data
    /// deslocado.
    /// </para>
    /// </summary>
    Task<CustomerStatementView> GetCustomerStatementAsync(
        Guid customerId, DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// A mesma conta de <see cref="GetNetRevenueAsync"/>, como série mensal
    /// em vez de um total — um ponto por mês civil dentro da janela,
    /// incluindo os que não tiveram nenhuma factura (a zero, para o
    /// consumidor desenhar uma série contínua sem buracos). Primeiro
    /// consumidor: Analytics & IA (módulo 10, "dashboards analíticos
    /// interactivos").
    /// </summary>
    Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetRevenueAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);
}

/// <param name="Year">Ano civil.</param>
/// <param name="Month">Mês, 1–12.</param>
public sealed record MonthlyAmount(int Year, int Month, decimal Amount);

public sealed record CustomerStatementView(
    decimal OpeningBalance,
    IReadOnlyList<CustomerStatementLine> Lines,
    decimal ClosingBalance);

/// <param name="Direction"><c>"Debit"</c> (factura) ou <c>"Credit"</c> (nota de crédito, recibo).</param>
public sealed record CustomerStatementLine(
    DateOnly Date,
    string DocumentType,
    string DocumentNumber,
    string Direction,
    decimal Amount,
    decimal BalanceAfter);

public sealed record CustomerRevenueView(Guid CustomerId, string CustomerName, decimal NetRevenue);

public sealed record CustomerInvoiceView(
    Guid InvoiceId,
    string Number,
    DateOnly IssuedOn,
    string Status,
    string Currency,
    decimal GrossTotal);

/// <summary>
/// Submissão de pagamento pelo próprio cliente — pedido de confirmação com
/// comprovativo bancário anexado, sem gateway (ADR-044). Primeiro consumidor
/// de escrita do Portal do Cliente, mesmo padrão que
/// <c>IApprovalGateway.SubmitAsync</c> já estabeleceu para "um módulo
/// submete algo a outro, que decide sozinho o que fazer com isso" — a
/// composição não pré-valida nada que `finance` já tenha de validar de
/// qualquer forma.
/// </summary>
public interface ICustomerPayments
{
    /// <param name="customerId">Resolvido pela composição a partir de `CurrentUser` — nunca vem do pedido do cliente.</param>
    /// <param name="documentId">O comprovativo, já carregado em `documents` (ADR-009).</param>
    /// <param name="submittedByUserId">A conta que submeteu, para o rasto de auditoria.</param>
    Task<SubmitPaymentClaimResult> SubmitClaimAsync(
        Guid customerId,
        Guid salesInvoiceId,
        decimal amount,
        DateOnly paidOn,
        Guid documentId,
        Guid submittedByUserId,
        string? notes,
        CancellationToken cancellationToken);

    /// <summary>Os pedidos do próprio cliente — "os meus comprovativos", com o estado de cada um.</summary>
    Task<IReadOnlyList<PaymentClaimView>> ListMyClaimsAsync(Guid customerId, CancellationToken cancellationToken);
}

public sealed record SubmitPaymentClaimResult(SubmitPaymentClaimOutcome Outcome, Guid? ClaimId, string? Error)
{
    public static SubmitPaymentClaimResult Submitted(Guid claimId) =>
        new(SubmitPaymentClaimOutcome.Submitted, claimId, null);

    public static SubmitPaymentClaimResult InvoiceNotFound() =>
        new(SubmitPaymentClaimOutcome.InvoiceNotFound, null, "Factura não encontrada.");

    public static SubmitPaymentClaimResult DocumentNotFound() =>
        new(SubmitPaymentClaimOutcome.DocumentNotFound, null, "Comprovativo não encontrado.");

    public static SubmitPaymentClaimResult ExceedsOutstanding(string error) =>
        new(SubmitPaymentClaimOutcome.ExceedsOutstanding, null, error);

    public static SubmitPaymentClaimResult Rejected(string error) =>
        new(SubmitPaymentClaimOutcome.Rejected, null, error);
}

public enum SubmitPaymentClaimOutcome
{
    Submitted,

    /// <summary>A factura não existe, ou não é deste cliente — a mesma resposta para os dois (404, não se revela a segunda).</summary>
    InvoiceNotFound,

    DocumentNotFound,

    /// <summary>Pede confirmação de mais do que está em aberto — 409.</summary>
    ExceedsOutstanding,

    /// <summary>Pedido malformado — 400.</summary>
    Rejected,
}

public sealed record PaymentClaimView(
    Guid Id,
    Guid SalesInvoiceId,
    decimal Amount,
    DateOnly PaidOn,
    string Status,
    string? RejectionReason,
    DateTimeOffset SubmittedAt);

/// <summary>
/// Leitura agregada de AP (Contas a Pagar) — despesa facturada e saldo em
/// aberto. Separada de <see cref="IReceivablesOverview"/> pela mesma razão
/// que <c>IPayablesStore</c> é separada de <c>ISalesInvoiceStore</c>
/// internamente: são dois contextos distintos, e juntá-los daria um
/// contrato que ninguém consegue implementar sem conhecer os dois.
/// </summary>
/// <summary>
/// As facturas de venda de um período, para relato fiscal. Primeiro (e por
/// agora único) consumidor: a secção <c>SalesInvoices</c> do SAF-T AO.
///
/// <para>
/// <strong>Separado de <see cref="IReceivablesOverview"/> de propósito.</strong>
/// Aquele dá números agregados — o que falta receber, quanto se facturou. Este
/// dá os documentos, linha a linha, porque um ficheiro de auditoria não é um
/// resumo.
/// </para>
/// </summary>
public interface ISalesInvoiceReporting
{
    /// <summary>
    /// Todas as facturas emitidas no período, <strong>incluindo as
    /// anuladas</strong>.
    ///
    /// <para>
    /// As anuladas vão com <c>InvoiceStatus</c> <c>A</c> e não desaparecem: é
    /// isso que BR-14 significa no ficheiro fiscal, e é como a AGT vê que um
    /// número foi emitido e depois anulado, em vez de ver um buraco na
    /// sequência.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ReportedInvoice>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// As notas de crédito emitidas no período, incluindo as anuladas.
    ///
    /// <para>
    /// <strong>Separadas das facturas porque somam ao contrário.</strong> No
    /// SAF-T as duas vivem na mesma secção <c>SalesInvoices</c>, mas uma
    /// factura credita e uma nota debita — <c>TotalCredit</c> e
    /// <c>TotalDebit</c> existem exactamente por isso. Misturá-las numa lista
    /// só obrigaria quem lê a descobrir de que lado está cada uma.
    /// </para>
    ///
    /// <para>
    /// ⚠ <strong>Omiti-las sobredeclara a receita.</strong> Uma nota de
    /// crédito reduz o que a factura pede, e um ficheiro que a esconde diz à
    /// AGT que se recebeu mais do que se recebeu.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ReportedCreditNote>> ListCreditNotesForPeriodAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Os recibos emitidos no período, incluindo os estornados. Vão à secção
    /// <c>Payments</c> do SAF-T.
    /// </summary>
    Task<IReadOnlyList<ReportedReceipt>> ListReceiptsForPeriodAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// As facturas de compra registadas no período.
    ///
    /// <para>
    /// ⚠ <strong>Sem as anuladas, ao contrário das de venda.</strong> A secção
    /// <c>PurchaseInvoices</c> do SAF-T <strong>não tem
    /// <c>DocumentStatus</c></strong> — não há onde escrever <c>A</c>. Incluir
    /// uma factura anulada sem poder dizer que o está sobredeclararia a
    /// compra; excluí-la é a única coisa que a secção sabe exprimir.
    /// </para>
    ///
    /// <para>
    /// A assimetria é do formato, não uma decisão nossa, e é por isso que fica
    /// escrita aqui: quem ler o ficheiro e contar documentos vai encontrar
    /// menos compras do que o Rivo mostra.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ReportedPurchase>> ListPurchasesForPeriodAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

/// <param name="Number">
/// O número que o <strong>fornecedor</strong> deu à factura — não um número
/// nosso. É o que vai a <c>InvoiceNo</c>.
/// </param>
/// <param name="SupplierId">
/// Nulo quando a factura foi registada sem se ligar a um fornecedor do
/// cadastro. O nome e o NIF ficam na mesma, e é a partir deles que o ficheiro
/// declara o fornecedor.
/// </param>
public sealed record ReportedPurchase(
    string Number,
    DateOnly IssuedOn,
    Guid? SupplierId,
    string SupplierName,
    string SupplierTaxId,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal);

/// <param name="Method">
/// Meio de pagamento <strong>no código do SAF-T</strong> — <c>NU</c>, <c>TB</c>,
/// <c>MB</c>… Não é rótulo escolhido aqui, e mudá-lo partiria o ficheiro.
/// </param>
/// <param name="Cancelled">Estornado. Continua no ficheiro, com estado <c>A</c>.</param>
public sealed record ReportedReceipt(
    string Number,
    DateOnly ReceivedOn,
    DateTimeOffset StatusDate,
    bool Cancelled,
    string? CancellationReason,
    Guid? CustomerId,
    string CustomerName,
    string CustomerTaxId,
    string CustomerAddressDetail,
    string CustomerCity,
    string CustomerCountry,
    string Method,
    decimal Total,
    IReadOnlyList<ReportedSettlement> Lines);

/// <param name="InvoiceDate">
/// A data da factura liquidada. <strong>Vem resolvida</strong> e não guardada na
/// linha: o SAF-T exige-a em <c>SourceDocumentID</c>, e copiá-la para a linha
/// do recibo seria a cópia que fica obsoleta em silêncio (BR-18).
/// </param>
public sealed record ReportedSettlement(
    int LineNumber,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    decimal Amount);

/// <param name="CorrectedInvoiceNumber">
/// O número da factura que esta nota corrige. Vai a <c>References</c> no
/// ficheiro — o SAF-T exige-o quando o tipo de documento é <c>NC</c>.
/// </param>
/// <param name="Invoice">
/// O resto, na mesma forma da factura: os dois documentos partilham quase
/// tudo, e o que muda é o sinal.
/// </param>
public sealed record ReportedCreditNote(
    string CorrectedInvoiceNumber,
    ReportedInvoice Invoice);

/// <param name="CustomerId">Nulo numa venda a consumidor final.</param>
/// <param name="Cancelled">
/// <c>true</c> quando anulada. Quem lê traduz para <c>N</c>/<c>A</c> — o
/// contrato não publica o enumerado interno (ADR-010).
/// </param>
/// <param name="StatusDate">
/// Quando o estado actual foi fixado: a anulação, se houve; o registo, se não.
/// </param>
/// <param name="Hash">
/// O elo da cadeia de integridade (ADR-060). <strong>Nulo nas facturas
/// emitidas antes de a cadeia existir</strong> — quem exporta decide o que
/// escrever, e não é aqui que se inventa um.
/// </param>
/// <param name="IssuedByUserId">Nulo pela mesma razão que <paramref name="Hash"/>.</param>
public sealed record ReportedInvoice(
    string Number,
    string Type,
    DateOnly IssuedOn,
    DateOnly TaxPointDate,
    DateTimeOffset SystemEntryDate,
    DateTimeOffset StatusDate,
    bool Cancelled,
    string? CancellationReason,
    Guid? CustomerId,

    // O cliente congelado na emissão, e não o de hoje. Vem inteiro porque há
    // clientes sem registo em `commercial` — uma venda a consumidor final —, e
    // é a única fonte do que o ficheiro fiscal tem de declarar sobre eles.
    string CustomerName,
    string CustomerTaxId,
    string CustomerAddressDetail,
    string CustomerCity,
    string CustomerCountry,
    Guid? IssuedByUserId,
    string? Hash,
    string Currency,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    IReadOnlyList<ReportedInvoiceLine> Lines);

public sealed record ReportedInvoiceLine(
    int LineNumber,
    string ProductCode,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal NetAmount,
    string TaxCode,
    decimal TaxPercentage);

public interface IPayablesOverview
{
    /// <summary>
    /// Despesa facturada no período: soma do valor líquido das facturas de
    /// compra <strong>registadas</strong> no período (regime de
    /// compromisso — quando a factura entra, não quando se paga), não
    /// anuladas. Simétrico a <see cref="IReceivablesOverview.GetNetRevenueAsync"/>:
    /// os dois lados do dashboard medem-se da mesma forma, ou "lucro"
    /// (receita − despesa) misturaria regimes sem ninguém reparar.
    /// </summary>
    Task<decimal> GetNetExpensesAsync(DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// O que falta pagar, agora, de todas as facturas de compra não
    /// anuladas nesta moeda — o total, menos o que já foi
    /// <strong>executado</strong> (pedidos só aceites ou submetidos não
    /// reduzem o que ainda se deve; o dinheiro não saiu).
    /// </summary>
    Task<decimal> GetOutstandingPayablesAsync(string currency, CancellationToken cancellationToken);

    /// <summary>A mesma conta de <see cref="GetNetExpensesAsync"/>, como série mensal — ver <see cref="IReceivablesOverview.GetMonthlyNetRevenueAsync"/>.</summary>
    Task<IReadOnlyList<MonthlyAmount>> GetMonthlyNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);
}

/// <summary>
/// Superfície publicada de `finance`. Assembly sem dependências (ADR-017).
///
/// <para>
/// <strong>Âmbito reduzido por ADR-036.</strong> Daqui saem a factura de venda,
/// o ciclo de recebimento, Contas a Pagar com Tesouraria, e o disponível
/// orçamental que BR-8 exige de `approval`.
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

    /// <summary>Consultar o plano de contas, lançamentos e balancete.</summary>
    public const string LedgerRead = "finance.ledger.read";

    /// <summary>
    /// Manter o plano de contas e lançar.
    ///
    /// <para>
    /// Separada de <see cref="LedgerClose"/> pela mesma razão que emitir é
    /// separado de anular: lançar é trabalho diário, fechar um período é acto
    /// que torna números definitivos.
    /// </para>
    /// </summary>
    public const string LedgerWrite = "finance.ledger.write";

    /// <summary>
    /// Fechar e <strong>reabrir</strong> períodos contabilísticos.
    ///
    /// <para>
    /// A reabertura é o que torna esta permissão perigosa: faz números já dados
    /// por definitivos voltarem a mexer-se. Fica com quem responde pelos
    /// livros, não com quem lança.
    /// </para>
    /// </summary>
    public const string LedgerClose = "finance.ledger.close";

    /// <summary>Consultar centros de custo, orçamentos e previsões.</summary>
    public const string PlanningRead = "finance.planning.read";

    /// <summary>Criar e rever centros de custo, orçamentos em rascunho e previsões.</summary>
    public const string PlanningWrite = "finance.planning.write";

    /// <summary>
    /// Aprovar um orçamento — pô-lo em vigor.
    ///
    /// <para>
    /// <strong>Separada de <see cref="PlanningWrite"/>, e é BR-8 na forma do
    /// catálogo.</strong> Quem elabora o orçamento não devia ser quem lhe dá
    /// força: senão bastava subir o tecto para o próprio pedido passar a caber,
    /// e a verificação orçamental deixaria de verificar o que quer que fosse.
    /// </para>
    /// </summary>
    public const string BudgetsApprove = "finance.budgets.approve";

    public static readonly IReadOnlyList<string> All =
    [
        InvoicesRead, InvoicesWrite, InvoicesCancel, SeriesWrite,
        ReceiptsRead, ReceiptsWrite,
        PayablesRead, PayablesWrite, PaymentsRequest, PaymentsExecute,
        LedgerRead, LedgerWrite, LedgerClose,
        PlanningRead, PlanningWrite, BudgetsApprove,
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

    /// <summary>
    /// O que um perfil de contabilidade recebe: manter o plano de contas e
    /// lançar. **Sem `LedgerClose`** — fechar e reabrir períodos torna números
    /// definitivos ou volta a mexê-los, e isso fica com `Admin`, pela mesma
    /// razão que abrir séries de documento fica.
    /// </summary>
    public static readonly IReadOnlyList<string> ForAccounting =
        [LedgerRead, LedgerWrite];

    /// <summary>
    /// Quem elabora orçamentos: escreve, **não aprova**.
    /// </summary>
    public static readonly IReadOnlyList<string> ForBudgetOwners =
        [PlanningRead, PlanningWrite];

    /// <summary>
    /// Quem põe um orçamento em vigor: aprova, **não escreve**.
    ///
    /// <para>
    /// As duas listas não se sobrepõem, e é isso que dá sentido a BR-8. Se
    /// fossem a mesma pessoa, bastaria subir o tecto para o próprio pedido
    /// passar a caber — e a verificação orçamental deixaria de verificar o que
    /// quer que fosse.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ForBudgetApprovers =
        [PlanningRead, BudgetsApprove];
}
