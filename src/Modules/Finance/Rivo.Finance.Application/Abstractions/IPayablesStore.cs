using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.Abstractions;

/// <summary>
/// Persistência de Contas a Pagar e Tesouraria. Separada de
/// <see cref="ISalesInvoiceStore"/> porque são dois contextos internos
/// distintos de `finance` (`modules/finance.md`) — juntá-los daria uma
/// interface que ninguém consegue implementar sem conhecer tudo.
/// </summary>
public interface IPayablesStore
{
    Task<BankAccount?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Rastreada. <strong>É aqui que a contenção acontece:</strong> quem procura
    /// assim vai sacar, e o contador de concorrência da conta é o que faz dois
    /// pagamentos simultâneos colidirem (BR-17).
    /// </summary>
    Task<BankAccount?> FindAccountForUpdateAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BankAccount>> ListAccountsAsync(
        bool includeClosed,
        CancellationToken cancellationToken);

    Task AddAccountAsync(BankAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Os movimentos de uma conta numa janela de datas, por ordem de ocorrência.
    ///
    /// <para>
    /// Método próprio e não a navegação do agregado: o caminho de escrita nunca
    /// carrega os movimentos, e carregá-los para ler seria trazer o histórico
    /// todo à memória para mostrar um mês.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<BankMovement>> ListMovementsAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);

    /// <summary>
    /// O saldo com que a janela abre: o <c>BalanceAfter</c> do último movimento
    /// <strong>anterior</strong> a <paramref name="from"/>.
    ///
    /// <para>
    /// Lido do movimento e não somado: se a soma e o saldo divergirem, é essa
    /// divergência que o extracto tem de mostrar, não esconder.
    /// </para>
    /// </summary>
    Task<decimal> OpeningBalanceAsync(
        Guid accountId,
        DateOnly? from,
        CancellationToken cancellationToken);

    Task<PurchaseInvoice?> FindPurchaseInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<PurchaseInvoice?> FindPurchaseInvoiceForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseInvoice>> ListPurchaseInvoicesAsync(
        DateOnly? dueBefore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verdadeiro se já existe uma factura com este número deste fornecedor.
    ///
    /// <para>
    /// Registar a mesma factura duas vezes é a forma mais comum de pagar a
    /// dobrar, e é invariante sobre o conjunto — o agregado não a vê.
    /// </para>
    /// </summary>
    Task<bool> PurchaseInvoiceExistsAsync(
        string supplierTaxId,
        string supplierInvoiceNumber,
        CancellationToken cancellationToken);

    Task AddPurchaseInvoiceAsync(PurchaseInvoice invoice, CancellationToken cancellationToken);

    Task<PaymentRequest?> FindPaymentRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<PaymentRequest?> FindPaymentRequestForUpdateAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentRequest>> ListPaymentRequestsAsync(
        Guid? purchaseInvoiceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quanto já está pedido ou pago sobre uma factura de compra, contando só os
    /// pedidos **não cancelados**.
    ///
    /// <para>
    /// Sem isto, três pedidos de metade cada sobre a mesma factura passariam os
    /// três — cada um cabe no total, e os três juntos pagam uma vez e meia.
    /// </para>
    /// </summary>
    Task<decimal> CommittedAsync(Guid purchaseInvoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Soma do valor líquido das facturas de compra registadas
    /// (<c>IssuedOn</c>) no período, nesta moeda, não anuladas. Primeiro
    /// consumidor: <c>IPayablesOverview</c> (Fase 8, ADR-041).
    /// </summary>
    Task<decimal> SumNetExpensesAsync(
        DateOnly from, DateOnly to, string currency, CancellationToken cancellationToken);

    /// <summary>
    /// O que falta pagar, agora, de todas as facturas de compra não
    /// anuladas nesta moeda — o total, menos só o que já foi
    /// <strong>executado</strong>. Diferente de <see cref="CommittedAsync"/>,
    /// que também conta pedidos ainda não executados — aqui o dinheiro tem
    /// de ter saído mesmo para deixar de contar como em falta.
    /// </summary>
    Task<decimal> SumOutstandingPayablesAsync(string currency, CancellationToken cancellationToken);

    Task AddPaymentRequestAsync(PaymentRequest request, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
