using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Finance.Application.UseCases;
using Rivo.Finance.Contracts;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Api;

/// <summary>
/// Contas a Pagar e Tesouraria. Vive à parte de
/// <see cref="FinanceModuleEndpoints"/> porque são dois contextos internos
/// distintos de `finance` (`modules/finance.md`).
/// </summary>
public static class PayablesEndpoints
{
    public static IEndpointRouteBuilder MapPayables(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/finance");

        // ---- Tesouraria ----
        group.MapGet("/accounts", ListAccountsAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        group.MapPost("/accounts", OpenAccountAsync)
            .RequireAuthorization(FinancePermissions.PayablesWrite);

        group.MapPost("/accounts/{accountId:guid}/deposits", DepositAsync)
            .RequireAuthorization(FinancePermissions.PayablesWrite);

        // O extracto. Leitura, e por isso a mesma permissão de consultar
        // contas — ver o que se moveu não é poder mover.
        group.MapGet("/accounts/{accountId:guid}/statement", GetStatementAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        // ---- Contas a Pagar ----
        group.MapGet("/purchase-invoices", ListPurchaseInvoicesAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        group.MapGet("/purchase-invoices/{purchaseInvoiceId:guid}", GetPurchaseInvoiceAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        group.MapPost("/purchase-invoices", RegisterPurchaseInvoiceAsync)
            .RequireAuthorization(FinancePermissions.PayablesWrite);

        // ---- Pedidos de pagamento ----
        group.MapGet("/payment-requests", ListPaymentRequestsAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        group.MapGet("/payment-requests/{paymentRequestId:guid}", GetPaymentRequestAsync)
            .RequireAuthorization(FinancePermissions.PayablesRead);

        // Pedir não é pagar. Quem pede não executa — ver FinancePermissions.
        group.MapPost("/payment-requests", CreatePaymentRequestAsync)
            .RequireAuthorization(FinancePermissions.PaymentsRequest);

        group.MapPost("/payment-requests/{paymentRequestId:guid}/cancellation", CancelPaymentRequestAsync)
            .RequireAuthorization(FinancePermissions.PaymentsRequest);

        // **O ponto de consistência forte do sistema.** A permissão abre a
        // porta; BR-1, BR-3 e BR-5 é que decidem.
        group.MapPost("/payment-requests/{paymentRequestId:guid}/execution", ExecutePaymentAsync)
            .RequireAuthorization(FinancePermissions.PaymentsExecute);

        return endpoints;
    }

    private static async Task<IResult> ListAccountsAsync(
        ListBankAccounts listAccounts,
        bool? includeClosed,
        CancellationToken cancellationToken) =>
        Results.Ok(await listAccounts.ExecuteAsync(includeClosed ?? false, cancellationToken));

    private static async Task<IResult> OpenAccountAsync(
        OpenAccountRequest request,
        OpenBankAccount openAccount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openAccount.ExecuteAsync(
            request.Name, request.Bank, request.Iban, request.Currency ?? "AOA",
            BuildAuditContext(http), cancellationToken);

        return result.Succeeded
            ? Results.Created($"/finance/accounts/{result.AccountId}", new { accountId = result.AccountId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["conta"] = [result.Error!] });
    }

    private static async Task<IResult> DepositAsync(
        Guid accountId,
        DepositRequest request,
        DepositToAccount deposit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await deposit.ExecuteAsync(
            accountId, request.Amount, request.Reference, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AccountMovementOutcome.Done => Results.NoContent(),
            AccountMovementOutcome.NotFound => Results.NotFound(new { erro = "Conta não encontrada." }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["deposito"] = ["O valor tem de ser maior que zero e a conta tem de estar aberta."],
            }),
        };
    }

    private static async Task<IResult> GetStatementAsync(
        Guid accountId,
        GetAccountStatement statement,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (from is { } inicio && to is { } fim && inicio > fim)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["periodo"] = ["A data inicial é posterior à final."],
            });
        }

        var extracto = await statement.ExecuteAsync(accountId, from, to, cancellationToken);

        return extracto is null
            ? Results.NotFound(new { erro = "Conta não encontrada." })
            : Results.Ok(extracto);
    }

    private static async Task<IResult> ListPurchaseInvoicesAsync(
        ListPurchaseInvoices listInvoices,
        DateOnly? dueBefore,
        CancellationToken cancellationToken) =>
        Results.Ok(await listInvoices.ExecuteAsync(dueBefore, cancellationToken));

    private static async Task<IResult> GetPurchaseInvoiceAsync(
        Guid purchaseInvoiceId,
        GetPurchaseInvoice getInvoice,
        CancellationToken cancellationToken)
    {
        var compra = await getInvoice.ExecuteAsync(purchaseInvoiceId, cancellationToken);

        return compra is null
            ? Results.NotFound(new { erro = "Factura de compra não encontrada." })
            : Results.Ok(compra);
    }

    private static async Task<IResult> RegisterPurchaseInvoiceAsync(
        RegisterPurchaseInvoiceRequest request,
        RegisterPurchaseInvoice registerInvoice,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await registerInvoice.ExecuteAsync(
            request.SupplierInvoiceNumber,
            request.SupplierName,
            request.SupplierTaxId,
            request.IssuedOn ?? hoje,
            request.DueOn ?? hoje,
            request.Currency ?? "AOA",
            request.NetTotal,
            request.TaxTotal,
            request.Description,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            RegisterPurchaseInvoiceOutcome.Registered => Results.Created(
                $"/finance/purchase-invoices/{result.PurchaseInvoiceId}",
                new { purchaseInvoiceId = result.PurchaseInvoiceId }),

            // 409: registar a mesma factura do mesmo fornecedor duas vezes é a
            // forma mais comum de pagar a dobrar.
            RegisterPurchaseInvoiceOutcome.Duplicate => Results.Conflict(new
            {
                erro = "Já existe uma factura com este número deste fornecedor.",
            }),

            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["factura"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> ListPaymentRequestsAsync(
        ListPaymentRequests listRequests,
        Guid? purchaseInvoiceId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listRequests.ExecuteAsync(purchaseInvoiceId, cancellationToken));

    private static async Task<IResult> GetPaymentRequestAsync(
        Guid paymentRequestId,
        GetPaymentRequest getRequest,
        CancellationToken cancellationToken)
    {
        var pedido = await getRequest.ExecuteAsync(paymentRequestId, cancellationToken);

        return pedido is null
            ? Results.NotFound(new { erro = "Pedido de pagamento não encontrado." })
            : Results.Ok(pedido);
    }

    private static async Task<IResult> CreatePaymentRequestAsync(
        CreatePaymentRequestRequest request,
        CreatePaymentRequest createRequest,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await createRequest.ExecuteAsync(
            request.PurchaseInvoiceId,
            request.Amount,
            request.RequestedByEmployeeId,
            request.RequestedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            request.Notes,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            // 202 e não 201: o pedido existe e **ainda não é pagável**. Passou
            // por governança e espera decisão — a mesma distinção que `hr` faz.
            CreatePaymentRequestOutcome.Created => Results.Accepted(
                $"/finance/payment-requests/{result.PaymentRequestId}",
                new
                {
                    paymentRequestId = result.PaymentRequestId,
                    approvalRequestId = result.ApprovalRequestId,
                    estado = "PendenteAprovacao",
                }),

            CreatePaymentRequestOutcome.InvoiceNotFound =>
                Results.NotFound(new { erro = "Factura de compra não encontrada." }),

            // 501: sem motor de governança a capacidade não existe. Melhor não
            // criar o pedido do que criar um que nunca poderá ser pago (BR-1).
            CreatePaymentRequestOutcome.ApprovalUnavailable => Results.Problem(
                "Não há motor de governança ligado. Sem decisão de aprovação não há pagamento (BR-1).",
                statusCode: StatusCodes.Status501NotImplemented),

            CreatePaymentRequestOutcome.ApprovalRefused =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            CreatePaymentRequestOutcome.ExceedsInvoice =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["pedido"] = [result.Error!] }),
        };
    }

    private static async Task<IResult> CancelPaymentRequestAsync(
        Guid paymentRequestId,
        CancelInvoiceRequest request,
        CancelPaymentRequest cancelRequest,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancelRequest.ExecuteAsync(
            paymentRequestId, request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            CancelInvoiceOutcome.Cancelled => Results.NoContent(),
            CancelInvoiceOutcome.NotFound => Results.NotFound(new { erro = "Pedido não encontrado." }),
            _ => Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<IResult> ExecutePaymentAsync(
        Guid paymentRequestId,
        ExecutePaymentRequest request,
        ExecutePayment executePayment,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var meio))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["method"] =
                [
                    $"'{request.Method}' não é meio de pagamento do SAF-T. " +
                    $"Válidos: {string.Join(", ", Enum.GetNames<PaymentMethod>())}.",
                ],
            });
        }

        var result = await executePayment.ExecuteAsync(
            paymentRequestId,
            request.BankAccountId,
            request.ExecutedByEmployeeId,
            meio,
            request.Reference,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            ExecutePaymentOutcome.Executed =>
                Results.Ok(new { estado = "Executado", saldoRestante = result.RemainingBalance }),

            ExecutePaymentOutcome.RequestNotFound =>
                Results.NotFound(new { erro = "Pedido de pagamento não encontrado." }),

            ExecutePaymentOutcome.AccountNotFound =>
                Results.NotFound(new { erro = "Conta bancária não encontrada." }),

            // **403 e não 409**: não é o estado que impede, é *esta pessoa*.
            // Mesma distinção que `approval` faz para BR-2 e BR-4.
            ExecutePaymentOutcome.SegregationOfDuties =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status403Forbidden),

            // 409 nas duas metades de BR-5: o pedido está bem formado, o que
            // não está é o estado — decisão em falta, ou saldo em falta.
            ExecutePaymentOutcome.NotApproved
                or ExecutePaymentOutcome.InsufficientFunds
                or ExecutePaymentOutcome.NotExecutable =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["pagamento"] = [result.Error!] }),
        };
    }

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }
}

public sealed record OpenAccountRequest(string Name, string Bank, string? Iban, string? Currency);

public sealed record DepositRequest(decimal Amount, string? Reference);

/// <param name="SupplierInvoiceNumber">
/// O número que o **fornecedor** pôs no documento dele. O Rivo não numera
/// facturas de compra.
/// </param>
public sealed record RegisterPurchaseInvoiceRequest(
    string SupplierInvoiceNumber,
    string SupplierName,
    string SupplierTaxId,
    DateOnly? IssuedOn,
    DateOnly? DueOn,
    string? Currency,
    decimal NetTotal,
    decimal TaxTotal,
    string? Description);

/// <param name="RequestedByEmployeeId">
/// Quem pede, como Colaborador de `hr`. É contra ele que BR-2 é verificada —
/// quem submete não decide.
/// </param>
public sealed record CreatePaymentRequestRequest(
    Guid PurchaseInvoiceId,
    decimal Amount,
    Guid RequestedByEmployeeId,
    DateOnly? RequestedOn,
    string? Notes);

/// <param name="ExecutedByEmployeeId">
/// Quem paga, como Colaborador. **Não pode ser nenhum dos que decidiram**
/// (BR-3), e é verificado no momento.
/// </param>
public sealed record ExecutePaymentRequest(
    Guid BankAccountId,
    Guid ExecutedByEmployeeId,
    string Method,
    string? Reference);
