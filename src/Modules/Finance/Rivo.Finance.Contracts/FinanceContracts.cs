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
}

public sealed record CustomerRevenueView(Guid CustomerId, string CustomerName, decimal NetRevenue);

public sealed record CustomerInvoiceView(
    Guid InvoiceId,
    string Number,
    DateOnly IssuedOn,
    string Status,
    string Currency,
    decimal GrossTotal);

/// <summary>
/// Leitura agregada de AP (Contas a Pagar) — despesa facturada e saldo em
/// aberto. Separada de <see cref="IReceivablesOverview"/> pela mesma razão
/// que <c>IPayablesStore</c> é separada de <c>ISalesInvoiceStore</c>
/// internamente: são dois contextos distintos, e juntá-los daria um
/// contrato que ninguém consegue implementar sem conhecer os dois.
/// </summary>
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
