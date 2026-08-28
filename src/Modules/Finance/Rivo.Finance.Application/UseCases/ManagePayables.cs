using Rivo.Audit.Contracts;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;
using Rivo.Procurement.Contracts;

namespace Rivo.Finance.Application.UseCases;

// ---------- Tesouraria ----------

public sealed class OpenBankAccount(IPayablesStore store, IAuditTrail audit)
{
    public async Task<OpenAccountResult> ExecuteAsync(
        string name,
        string bank,
        string? iban,
        string currency,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        BankAccount conta;

        try
        {
            conta = BankAccount.Open(name, bank, iban, currency);
        }
        catch (ArgumentException error)
        {
            return OpenAccountResult.Rejected(error.Message);
        }

        await store.AddAccountAsync(conta, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountOpened,
                FinanceAuditEntityTypes.BankAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{conta.Name}}","bank":"{{conta.Bank}}","currency":"{{conta.Currency}}"}"""),
            cancellationToken);

        return OpenAccountResult.Success(conta.Id);
    }
}

public sealed record OpenAccountResult(bool Succeeded, Guid? AccountId, string? Error)
{
    public static OpenAccountResult Success(Guid id) => new(true, id, null);

    public static OpenAccountResult Rejected(string error) => new(false, null, error);
}

/// <summary>
/// Entrada de fundos numa conta.
///
/// <para>
/// <strong>Não é o recebimento de uma factura</strong> — é o carregamento da
/// conta. Ligar os dois é Contabilidade & Fecho, que não existe; misturá-los
/// aqui faria o saldo bater por acidente e depois deixar de bater.
/// </para>
/// </summary>
public sealed class DepositToAccount(IPayablesStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<AccountMovementOutcome> ExecuteAsync(
        Guid accountId,
        decimal amount,
        string? reference,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var conta = await store.FindAccountForUpdateAsync(accountId, cancellationToken);

        if (conta is null)
        {
            return AccountMovementOutcome.NotFound;
        }

        try
        {
            conta.Deposit(amount, clock.GetUtcNow(), reference);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return AccountMovementOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountDeposited,
                FinanceAuditEntityTypes.BankAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"amount":{{amount}},"balance":{{conta.Balance}},"reference":"{{reference}}"}"""),
            cancellationToken);

        return AccountMovementOutcome.Done;
    }
}

public enum AccountMovementOutcome
{
    Done,
    NotFound,
    Rejected,
}

/// <summary>
/// Saída de conta que <strong>não</strong> é pagamento a fornecedor —
/// comissões bancárias, transferências entre contas.
///
/// <para>
/// O pagamento propriamente dito passa por <c>ExecutePayment</c>, que impõe a
/// dupla barreira de BR-5. Esta rota é para o resto do que sai de uma conta
/// sem ter passado por uma decisão de aprovação — e não substitui nem contorna
/// aquela.
/// </para>
/// </summary>
public sealed class WithdrawFromAccount(IPayablesStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<AccountMovementOutcome> ExecuteAsync(
        Guid accountId,
        decimal amount,
        string description,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var conta = await store.FindAccountForUpdateAsync(accountId, cancellationToken);

        if (conta is null)
        {
            return AccountMovementOutcome.NotFound;
        }

        try
        {
            // Sem `sourceType`/`sourceId`: não vem de documento nenhum, ao
            // contrário do que `ExecutePayment` regista.
            conta.Withdraw(amount, clock.GetUtcNow(), description);
        }
        catch (Exception error)
            when (error is ArgumentException or ArgumentOutOfRangeException
                or InsufficientFundsException or InvalidOperationException)
        {
            return AccountMovementOutcome.Rejected;
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.AccountWithdrawn,
                FinanceAuditEntityTypes.BankAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"amount":{{amount}},"balance":{{conta.Balance}},"description":"{{description}}"}"""),
            cancellationToken);

        return AccountMovementOutcome.Done;
    }
}

/// <summary>
/// Fecha ou reabre uma conta bancária. Nunca elimina — BR-14.
/// </summary>
public sealed class SetBankAccountStatus(IPayablesStore store, IAuditTrail audit)
{
    public async Task<SetBankAccountStatusResult> ExecuteAsync(
        Guid accountId,
        bool active,
        string? reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var conta = await store.FindAccountForUpdateAsync(accountId, cancellationToken);

        if (conta is null)
        {
            return SetBankAccountStatusResult.NotFound();
        }

        if (active)
        {
            conta.Reopen();
        }
        else
        {
            // A recusa por saldo diferente de zero é do domínio — é invariante
            // de uma conta só, e não desta camada.
            try
            {
                conta.Close();
            }
            catch (InvalidOperationException error)
            {
                return SetBankAccountStatusResult.Rejected(error.Message);
            }
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                active ? FinanceAuditActions.AccountReopened : FinanceAuditActions.AccountClosed,
                FinanceAuditEntityTypes.BankAccount,
                conta.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{reason}}"}"""),
            cancellationToken);

        return SetBankAccountStatusResult.Changed();
    }
}

public sealed record SetBankAccountStatusResult(BankAccountStatusOutcome Outcome, string? Error)
{
    public static SetBankAccountStatusResult Changed() => new(BankAccountStatusOutcome.Changed, null);

    public static SetBankAccountStatusResult NotFound() => new(BankAccountStatusOutcome.NotFound, null);

    public static SetBankAccountStatusResult Rejected(string error) =>
        new(BankAccountStatusOutcome.Rejected, error);
}

public enum BankAccountStatusOutcome
{
    Changed,
    NotFound,

    /// <summary>Fechar com saldo diferente de zero. 409.</summary>
    Rejected,
}

public sealed class ListBankAccounts(IPayablesStore store)
{
    public async Task<IReadOnlyList<BankAccountView>> ExecuteAsync(
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var contas = await store.ListAccountsAsync(includeClosed, cancellationToken);

        return [.. contas.Select(c => new BankAccountView(
            c.Id, c.Name, c.Bank, c.Iban, c.Currency, c.Balance, c.IsActive))];
    }
}

public sealed record BankAccountView(
    Guid AccountId,
    string Name,
    string Bank,
    string? Iban,
    string Currency,
    decimal Balance,
    bool IsActive);

/// <summary>
/// O extracto de uma conta: com que saldo a janela abre, o que se moveu, e com
/// que saldo fecha.
///
/// <para>
/// <strong>É o que torna a reconciliação bancária possível.</strong> Confrontar
/// o Rivo com o banco é comparar movimentos, e até aqui só havia um saldo —
/// um número sem explicação de como lá chegou.
/// </para>
///
/// <para>
/// A conta continua a ser a fonte do saldo corrente. O extracto não o
/// recalcula: <strong>expõe os dois lado a lado</strong> em
/// <see cref="AccountStatementView.Reconciles"/>, para que uma divergência
/// apareça em vez de ser absorvida.
/// </para>
/// </summary>
public sealed class GetAccountStatement(IPayablesStore store)
{
    public async Task<AccountStatementView?> ExecuteAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var conta = await store.FindAccountAsync(accountId, cancellationToken);

        if (conta is null)
        {
            return null;
        }

        var abertura = await store.OpeningBalanceAsync(accountId, from, cancellationToken);
        var movimentos = await store.ListMovementsAsync(accountId, from, to, cancellationToken);

        var creditos = movimentos
            .Where(m => m.Direction is BankMovementDirection.Credit)
            .Sum(m => m.Amount);

        var debitos = movimentos
            .Where(m => m.Direction is BankMovementDirection.Debit)
            .Sum(m => m.Amount);

        // O fecho vem do último movimento, não da soma. Se a cadeia de
        // `BalanceAfter` estiver partida, é aqui que se vê.
        var fecho = movimentos.Count > 0 ? movimentos[^1].BalanceAfter : abertura;

        return new AccountStatementView(
            conta.Id,
            conta.Name,
            conta.Bank,
            conta.Currency,
            from,
            to,
            abertura,
            creditos,
            debitos,
            fecho,
            conta.Balance,

            // Só se pode afirmar reconciliação sobre uma janela que chega ao
            // presente. Num extracto de Março, o fecho *não deve* bater com o
            // saldo de hoje — e dizer que não reconcilia seria mentir ao
            // contrário.
            Reconciles: to is null ? fecho == conta.Balance : null,

            [.. movimentos.Select(m => new BankMovementView(
                m.Id,
                m.OccurredAt,
                m.Direction.ToString(),
                m.Amount,
                m.BalanceAfter,
                m.Description,
                m.SourceType,
                m.SourceId))]);
    }
}

/// <param name="AccountBalance">O saldo corrente da conta, para comparação.</param>
/// <param name="Reconciles">
/// Nulo quando a janela tem fim — a pergunta não se aplica.
/// </param>
public sealed record AccountStatementView(
    Guid AccountId,
    string Name,
    string Bank,
    string Currency,
    DateOnly? From,
    DateOnly? To,
    decimal OpeningBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal ClosingBalance,
    decimal AccountBalance,
    bool? Reconciles,
    IReadOnlyList<BankMovementView> Movements);

public sealed record BankMovementView(
    Guid MovementId,
    DateTimeOffset OccurredAt,
    string Direction,
    decimal Amount,
    decimal BalanceAfter,
    string Description,
    string? SourceType,
    Guid? SourceId);

// ---------- Contas a Pagar ----------

public sealed class RegisterPurchaseInvoice(
    IPayablesStore store,
    ISupplierDirectory suppliers,
    IPurchaseOrderDirectory orders,
    IAuditTrail audit,
    PostDocument posting,
    TimeProvider clock)
{
    /// <param name="supplierId">
    /// Quando quem regista já sabe o fornecedor (escolhido numa lista), liga
    /// directamente — e um identificador que não existe em `procurement` é
    /// recusado, porque quem chama afirmou uma ligação que não é verdade.
    ///
    /// <para>
    /// <strong>Nulo é o caso comum.</strong> Quem tem a factura em papel não
    /// tem o identificador, só o NIF — tenta-se ligar por
    /// <see cref="ISupplierDirectory.FindByTaxIdAsync"/>, e não encontrar não é
    /// erro: nem toda a despesa passa por um Fornecedor qualificado em
    /// `procurement` (uma factura de electricidade, por exemplo).
    /// </para>
    /// </param>
    /// <param name="purchaseOrderId">
    /// A Ordem de Compra que esta factura acerta. Opcional — nem toda a
    /// factura tem uma. Indicada, tem de existir e ser do mesmo fornecedor;
    /// discrepância de quantidade ou valor não é recusada aqui — fica visível
    /// em <c>GetPurchaseInvoiceMatch</c>, para quem decide olhar.
    /// </param>
    public async Task<RegisterPurchaseInvoiceResult> ExecuteAsync(
        string supplierInvoiceNumber,
        Guid? supplierId,
        Guid? purchaseOrderId,
        string supplierName,
        string supplierTaxId,
        DateOnly issuedOn,
        DateOnly dueOn,
        string currency,
        decimal netTotal,
        decimal taxTotal,
        string? description,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        Guid? fornecedorLigado;

        if (supplierId is Guid idIndicado)
        {
            if (await suppliers.FindAsync(idIndicado, cancellationToken) is null)
            {
                return RegisterPurchaseInvoiceResult.Rejected(
                    "O fornecedor indicado não existe em procurement.");
            }

            fornecedorLigado = idIndicado;
        }
        else
        {
            fornecedorLigado = (await suppliers.FindByTaxIdAsync(supplierTaxId, cancellationToken))?.SupplierId;
        }

        if (purchaseOrderId is Guid idOrdem)
        {
            var ordem = await orders.FindAsync(idOrdem, cancellationToken);

            if (ordem is null)
            {
                return RegisterPurchaseInvoiceResult.Rejected(
                    "A ordem de compra indicada não existe em procurement.");
            }

            if (fornecedorLigado is Guid ligado && ligado != ordem.SupplierId)
            {
                return RegisterPurchaseInvoiceResult.Rejected(
                    "A ordem de compra indicada não é deste fornecedor.");
            }

            // A ordem sabe o fornecedor com certeza — se a factura ainda não
            // estava ligada a nenhum, herda-o daqui.
            fornecedorLigado ??= ordem.SupplierId;
        }

        PurchaseInvoice compra;

        try
        {
            compra = PurchaseInvoice.Register(
                supplierInvoiceNumber,
                fornecedorLigado,
                purchaseOrderId,
                new PayeeParty(supplierName, supplierTaxId),
                issuedOn, dueOn, currency, netTotal, taxTotal, description);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            return RegisterPurchaseInvoiceResult.Rejected(error.Message);
        }

        // Registar a mesma factura duas vezes é a forma mais comum de pagar a
        // dobrar. O agregado não vê o conjunto; esta camada vê.
        if (await store.PurchaseInvoiceExistsAsync(
                compra.SupplierTaxId, compra.SupplierInvoiceNumber, cancellationToken))
        {
            return RegisterPurchaseInvoiceResult.Duplicate();
        }

        await store.AddPurchaseInvoiceAsync(compra, cancellationToken);

        // O número é do fornecedor, e é ele que vai para o número de arquivo
        // do SAF-T: é assim que se encontra o documento físico.
        var lancamento = await posting.PostAsync(
            new DocumentPosting(
                PostingEvent.PurchaseInvoiceRegistered,
                compra.SupplierInvoiceNumber,

                // **O número é do fornecedor, e não é único.** Dois
                // fornecedores emitem `FT 100` no mesmo dia sem nada de
                // errado — a chave do SAF-T colidiria. Usa-se a identidade do
                // registo; o número do fornecedor fica na descrição e nas
                // linhas, que é onde se procura.
                DocumentPosting.KeyFor("FC", compra.Id),
                $"Compra a {compra.SupplierName}",
                compra.IssuedOn,
                compra.NetTotal,
                compra.TaxTotal,
                compra.GrossTotal,
                PostingSources.Automatic,
                clock.GetUtcNow()),
            cancellationToken);

        if (lancamento.Outcome is DocumentPostingOutcome.PeriodClosed or DocumentPostingOutcome.Failed)
        {
            return RegisterPurchaseInvoiceResult.PostingBlocked(lancamento.Error!);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PurchaseInvoiceRegistered,
                FinanceAuditEntityTypes.PurchaseInvoice,
                compra.Id.ToString(),
                context,
                NewValue: $$"""{"number":"{{compra.SupplierInvoiceNumber}}","supplierTaxId":"{{compra.SupplierTaxId}}","grossTotal":{{compra.GrossTotal}}}"""),
            cancellationToken);

        return RegisterPurchaseInvoiceResult.Success(compra.Id);
    }
}

public sealed record RegisterPurchaseInvoiceResult(
    RegisterPurchaseInvoiceOutcome Outcome,
    Guid? PurchaseInvoiceId,
    string? Error)
{
    public static RegisterPurchaseInvoiceResult Success(Guid id) =>
        new(RegisterPurchaseInvoiceOutcome.Registered, id, null);

    public static RegisterPurchaseInvoiceResult Duplicate() =>
        new(RegisterPurchaseInvoiceOutcome.Duplicate, null, null);

    /// <summary>Postagem automática ligada e falhada. A factura não é registada.</summary>
    public static RegisterPurchaseInvoiceResult PostingBlocked(string error) =>
        new(RegisterPurchaseInvoiceOutcome.PostingBlocked, null, error);

    public static RegisterPurchaseInvoiceResult Rejected(string error) =>
        new(RegisterPurchaseInvoiceOutcome.Rejected, null, error);
}

public enum RegisterPurchaseInvoiceOutcome
{
    Registered,
    Duplicate,

    /// <summary>Contabilidade automática ligada e a postagem falhou — 409.</summary>
    PostingBlocked,

    Rejected,
}

public sealed class ListPurchaseInvoices(IPayablesStore store)
{
    public async Task<IReadOnlyList<PurchaseInvoiceView>> ExecuteAsync(
        DateOnly? dueBefore,
        CancellationToken cancellationToken)
    {
        var compras = await store.ListPurchaseInvoicesAsync(dueBefore, cancellationToken);

        return [.. compras.Select(ToView)];
    }

    internal static PurchaseInvoiceView ToView(PurchaseInvoice compra) =>
        new(
            compra.Id,
            compra.SupplierInvoiceNumber,
            compra.SupplierId,
            compra.PurchaseOrderId,
            compra.SupplierName,
            compra.SupplierTaxId,
            compra.IssuedOn,
            compra.DueOn,
            compra.Currency,
            compra.NetTotal,
            compra.TaxTotal,
            compra.GrossTotal,
            compra.Status.ToString(),
            compra.Description,
            compra.CancelledAt,
            compra.CancellationReason);
}

public sealed record PurchaseInvoiceView(
    Guid PurchaseInvoiceId,
    string SupplierInvoiceNumber,
    Guid? SupplierId,
    Guid? PurchaseOrderId,
    string SupplierName,
    string SupplierTaxId,
    DateOnly IssuedOn,
    DateOnly DueOn,
    string Currency,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    string Status,
    string? Description,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

public sealed class GetPurchaseInvoice(IPayablesStore store)
{
    public async Task<PurchaseInvoiceView?> ExecuteAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var compra = await store.FindPurchaseInvoiceAsync(invoiceId, cancellationToken);

        return compra is null ? null : ListPurchaseInvoices.ToView(compra);
    }
}

/// <summary>
/// O 3-way match, só do lado que se pode comparar sem inventar regra: os
/// totais lado a lado. <strong>Não recusa nada e não decide se "bate"</strong>
/// — a tolerância de desvio é decisão de negócio sem fonte neste repositório
/// (mesma ressalva do desvio sobre a alçada em `procurement`), e um limiar
/// escolhido aqui seria inventá-la.
/// </summary>
public sealed class GetPurchaseInvoiceMatch(IPayablesStore store, IPurchaseOrderDirectory orders)
{
    public async Task<PurchaseInvoiceMatchView?> ExecuteAsync(Guid purchaseInvoiceId, CancellationToken cancellationToken)
    {
        var compra = await store.FindPurchaseInvoiceAsync(purchaseInvoiceId, cancellationToken);

        if (compra is null)
        {
            return null;
        }

        if (compra.PurchaseOrderId is not Guid idOrdem)
        {
            return new PurchaseInvoiceMatchView(
                compra.Id, null, null, null, compra.NetTotal, compra.GrossTotal, []);
        }

        var ordem = await orders.FindAsync(idOrdem, cancellationToken);

        // Não devia acontecer — uma ordem nunca se elimina (BR-14) —, mas ler
        // o que veio em vez de assumir que está lá é sempre mais seguro.
        if (ordem is null)
        {
            return new PurchaseInvoiceMatchView(
                compra.Id, idOrdem, null, null, compra.NetTotal, compra.GrossTotal, []);
        }

        var linhas = ordem.Lines
            .Select(l => new PurchaseOrderMatchLine(
                l.LineId, l.Description, l.QuantityOrdered, l.QuantityReceived, l.UnitPrice, l.LineTotal))
            .ToList();

        return new PurchaseInvoiceMatchView(
            compra.Id,
            ordem.PurchaseOrderId,
            ordem.Total,
            linhas.Sum(l => l.UnitPrice * l.QuantityReceived),
            compra.NetTotal,
            compra.GrossTotal,
            linhas);
    }
}

/// <param name="OrderedTotal">
/// Soma das linhas da ordem ao preço acordado. Nulo sem ordem ligada.
/// </param>
/// <param name="ReceivedTotal">
/// Quantidade recebida valorizada ao preço acordado — o segundo lado do
/// match. Nulo sem ordem ligada.
/// </param>
/// <param name="InvoicedNetTotal">
/// O que a factura diz, sem imposto — compara-se com <see cref="OrderedTotal"/>
/// e <see cref="ReceivedTotal"/>, que também não o têm.
/// </param>
public sealed record PurchaseInvoiceMatchView(
    Guid PurchaseInvoiceId,
    Guid? PurchaseOrderId,
    decimal? OrderedTotal,
    decimal? ReceivedTotal,
    decimal InvoicedNetTotal,
    decimal InvoicedGrossTotal,
    IReadOnlyList<PurchaseOrderMatchLine> Lines);

public sealed record PurchaseOrderMatchLine(
    Guid LineId,
    string Description,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal UnitPrice,
    decimal LineTotal);

// ---------- Pedidos de pagamento ----------

/// <summary>
/// Cria um pedido de pagamento **e submete-o a governança no mesmo acto**.
///
/// <para>
/// Os dois passos são um só de propósito: BR-1 não admite pagamento sem decisão
/// registada, e criar primeiro para submeter depois deixaria uma janela em que
/// existe um pedido pagável sem processo.
/// </para>
/// </summary>
public sealed class CreatePaymentRequest(
    IPayablesStore store,
    IPlanningStore planning,
    IPaymentApproval approval,
    IAuditTrail audit)
{
    /// <param name="costCentreId">
    /// A que centro de custo a despesa é imputada. <strong>É o que faz o pedido
    /// consumir orçamento</strong> — sem imputação, BR-8 não tem contra que
    /// verificar, e uma política que a exija recusa a submissão.
    /// </param>
    public async Task<CreatePaymentRequestResult> ExecuteAsync(
        Guid purchaseInvoiceId,
        decimal amount,
        Guid requestedByEmployeeId,
        DateOnly requestedOn,
        Guid? costCentreId,
        string? notes,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Sem governança não se cria. Um pedido que nunca pudesse ser aprovado
        // seria dívida a fingir que está a caminho.
        if (!approval.IsAvailable)
        {
            return CreatePaymentRequestResult.ApprovalUnavailable();
        }

        var compra = await store.FindPurchaseInvoiceAsync(purchaseInvoiceId, cancellationToken);

        if (compra is null)
        {
            return CreatePaymentRequestResult.InvoiceNotFound();
        }

        // Três pedidos de metade cada passariam um a um. Juntos pagam uma vez e
        // meia — é a invariante sobre o conjunto que o agregado não vê.
        var comprometido = await store.CommittedAsync(purchaseInvoiceId, cancellationToken);
        var disponivel = compra.GrossTotal - comprometido;

        if (amount > disponivel)
        {
            return CreatePaymentRequestResult.ExceedsInvoice(
                $"A factura {compra.SupplierInvoiceNumber} é de {compra.GrossTotal:N2}, já tem " +
                $"{comprometido:N2} em pedidos, e este é de {amount:N2}.");
        }

        CostCentre? centro = null;

        if (costCentreId is { } rubrica)
        {
            centro = await planning.FindCostCentreAsync(rubrica, cancellationToken);

            if (centro is null || !centro.IsActive)
            {
                return CreatePaymentRequestResult.CostCentreNotFound();
            }
        }

        var submissao = await approval.SubmitAsync(
            // O identificador do pedido ainda não existe — gera-se depois de a
            // submissão correr bem. Usa-se o da factura como referência de
            // origem, que é o que `approval` guarda e devolve sem interpretar.
            purchaseInvoiceId,
            requestedByEmployeeId,
            amount,
            compra.Currency,

            // O departamento do centro de custo, para escolher a política. Vem
            // da imputação e não de um campo à parte: são a mesma decisão.
            centro?.DepartmentId,

            // A rubrica, para BR-8. `approval` transporta-a sem a interpretar e
            // devolve-a a quem sabe lê-la — a mesma coisa que faz com a
            // referência de origem.
            centro?.Id.ToString(),
            $"Pagamento de {amount:N2} {compra.Currency} a {compra.SupplierName}, " +
            $"factura {compra.SupplierInvoiceNumber}",
            cancellationToken);

        if (!submissao.Submitted)
        {
            return CreatePaymentRequestResult.ApprovalRefused(submissao.Reason!);
        }

        PaymentRequest pedido;

        try
        {
            pedido = PaymentRequest.Create(
                compra, amount, requestedByEmployeeId, submissao.RequestId!.Value, requestedOn,
                centro?.Id, notes);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return CreatePaymentRequestResult.Rejected(error.Message);
        }

        await store.AddPaymentRequestAsync(pedido, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentRequested,
                FinanceAuditEntityTypes.PaymentRequest,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"amount":{{pedido.Amount}},"currency":"{{pedido.Currency}}","invoice":"{{pedido.SupplierInvoiceNumber}}","approvalRequest":"{{pedido.ApprovalRequestId}}"}"""),
            cancellationToken);

        return CreatePaymentRequestResult.Success(pedido.Id, pedido.ApprovalRequestId);
    }
}

public sealed record CreatePaymentRequestResult(
    CreatePaymentRequestOutcome Outcome,
    Guid? PaymentRequestId,
    Guid? ApprovalRequestId,
    string? Error)
{
    public static CreatePaymentRequestResult Success(Guid id, Guid approvalId) =>
        new(CreatePaymentRequestOutcome.Created, id, approvalId, null);

    public static CreatePaymentRequestResult InvoiceNotFound() =>
        new(CreatePaymentRequestOutcome.InvoiceNotFound, null, null, null);

    public static CreatePaymentRequestResult CostCentreNotFound() =>
        new(CreatePaymentRequestOutcome.CostCentreNotFound, null, null, null);

    public static CreatePaymentRequestResult ApprovalUnavailable() =>
        new(CreatePaymentRequestOutcome.ApprovalUnavailable, null, null, null);

    public static CreatePaymentRequestResult ApprovalRefused(string error) =>
        new(CreatePaymentRequestOutcome.ApprovalRefused, null, null, error);

    public static CreatePaymentRequestResult ExceedsInvoice(string error) =>
        new(CreatePaymentRequestOutcome.ExceedsInvoice, null, null, error);

    public static CreatePaymentRequestResult Rejected(string error) =>
        new(CreatePaymentRequestOutcome.Rejected, null, null, error);
}

public enum CreatePaymentRequestOutcome
{
    Created,
    InvoiceNotFound,

    /// <summary>A rubrica indicada não existe ou está desactivada — 404.</summary>
    CostCentreNotFound,

    /// <summary>Sem motor de governança. 501 — a capacidade não existe.</summary>
    ApprovalUnavailable,

    /// <summary>Sem política aplicável, empate, ou sem aprovadores. 409.</summary>
    ApprovalRefused,

    /// <summary>Já há pedidos que cobrem a factura. 409.</summary>
    ExceedsInvoice,

    Rejected,
}

public sealed class ListPaymentRequests(IPayablesStore store)
{
    public async Task<IReadOnlyList<PaymentRequestView>> ExecuteAsync(
        Guid? purchaseInvoiceId,
        CancellationToken cancellationToken)
    {
        var pedidos = await store.ListPaymentRequestsAsync(purchaseInvoiceId, cancellationToken);

        return [.. pedidos.Select(ToView)];
    }

    internal static PaymentRequestView ToView(PaymentRequest p) =>
        new(
            p.Id,
            p.PurchaseInvoiceId,
            p.SupplierInvoiceNumber,
            p.Payee.Name,
            p.Payee.TaxId,
            p.Amount,
            p.Currency,
            p.Status.ToString(),
            p.RequestedByEmployeeId,
            p.RequestedOn,
            p.ApprovalRequestId,
            p.ExecutedFromAccountId,
            p.ExecutedByEmployeeId,
            p.ExecutedAt,
            p.ExecutedMethod?.ToString(),
            p.ExecutionReference,
            p.Notes,
            p.CancelledAt,
            p.CancellationReason);
}

/// <param name="ApprovalRequestId">
/// O processo em `approval`. **O estado dele não vem aqui** — consulta-se em
/// `/approval/requests/{id}`, porque copiá-lo seria guardar uma verdade que é
/// de outro módulo e que fica obsoleta em silêncio.
/// </param>
public sealed record PaymentRequestView(
    Guid PaymentRequestId,
    Guid PurchaseInvoiceId,
    string SupplierInvoiceNumber,
    string PayeeName,
    string PayeeTaxId,
    decimal Amount,
    string Currency,
    string Status,
    Guid RequestedByEmployeeId,
    DateOnly RequestedOn,
    Guid ApprovalRequestId,
    Guid? ExecutedFromAccountId,
    Guid? ExecutedByEmployeeId,
    DateTimeOffset? ExecutedAt,
    string? ExecutedMethod,
    string? ExecutionReference,
    string? Notes,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

public sealed class GetPaymentRequest(IPayablesStore store)
{
    public async Task<PaymentRequestView?> ExecuteAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var pedido = await store.FindPaymentRequestAsync(requestId, cancellationToken);

        return pedido is null ? null : ListPaymentRequests.ToView(pedido);
    }
}

public sealed class CancelPaymentRequest(IPayablesStore store, IAuditTrail audit, TimeProvider clock)
{
    public async Task<CancelInvoiceResult> ExecuteAsync(
        Guid requestId,
        string reason,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var pedido = await store.FindPaymentRequestForUpdateAsync(requestId, cancellationToken);

        if (pedido is null)
        {
            return CancelInvoiceResult.NotFound();
        }

        try
        {
            pedido.Cancel(reason, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return CancelInvoiceResult.Rejected(error.Message);
        }

        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                FinanceAuditActions.PaymentRequestCancelled,
                FinanceAuditEntityTypes.PaymentRequest,
                pedido.Id.ToString(),
                context,
                NewValue: $$"""{"reason":"{{pedido.CancellationReason}}"}"""),
            cancellationToken);

        return CancelInvoiceResult.Success();
    }
}
