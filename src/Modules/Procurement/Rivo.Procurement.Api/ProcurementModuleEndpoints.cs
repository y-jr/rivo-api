using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Procurement.Application.UseCases;
using Rivo.Procurement.Contracts;

namespace Rivo.Procurement.Api;

public static class ProcurementModuleEndpoints
{
    public static IEndpointRouteBuilder MapProcurementModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/procurement");

        group.MapGet("/suppliers", ListSuppliersAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersRead);

        group.MapGet("/suppliers/{supplierId:guid}", GetSupplierAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersRead);

        group.MapPost("/suppliers", RegisterSupplierAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersWrite);

        group.MapPost("/suppliers/{supplierId:guid}/details", UpdateSupplierAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersWrite);

        // Desactivar, nunca eliminar (BR-14). Não há DELETE nesta superfície.
        group.MapPost("/suppliers/{supplierId:guid}/status", SetSupplierStatusAsync)
            .RequireAuthorization(ProcurementPermissions.SuppliersWrite);

        group.MapGet("/requisitions", ListRequisitionsAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsRead);

        group.MapGet("/requisitions/{requisitionId:guid}", GetRequisitionAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsRead);

        group.MapPost("/requisitions", OpenRequisitionAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsWrite);

        // Submeter é acto separado de abrir: é ele que congela o que se pede e
        // manda para decisão.
        group.MapPost("/requisitions/{requisitionId:guid}/submission", SubmitRequisitionAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsWrite);

        // Ler a decisão de `approval` e aplicá-la. Mesma forma que `hr` usa em
        // `/hr/leave/{id}/approval-outcome`.
        group.MapPost("/requisitions/{requisitionId:guid}/approval-outcome", ApplyDecisionAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsRead);

        group.MapPost("/requisitions/{requisitionId:guid}/cancellation", CancelRequisitionAsync)
            .RequireAuthorization(ProcurementPermissions.RequisitionsWrite);

        group.MapGet("/orders", ListOrdersAsync)
            .RequireAuthorization(ProcurementPermissions.OrdersRead);

        group.MapGet("/orders/{purchaseOrderId:guid}", GetOrderAsync)
            .RequireAuthorization(ProcurementPermissions.OrdersRead);

        // A ordem nasce da requisição, e a rota di-lo: nao ha `POST /orders`
        // avulso, porque nao ha ordem avulsa.
        group.MapPost("/requisitions/{requisitionId:guid}/orders", IssueOrderAsync)
            .RequireAuthorization(ProcurementPermissions.OrdersWrite);

        group.MapPost("/orders/{purchaseOrderId:guid}/cancellation", CancelOrderAsync)
            .RequireAuthorization(ProcurementPermissions.OrdersWrite);

        return endpoints;
    }

    private static async Task<IResult> ListSuppliersAsync(
        ListSuppliers listSuppliers,
        bool? includeInactive,
        CancellationToken cancellationToken) =>
        Results.Ok(await listSuppliers.ExecuteAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> GetSupplierAsync(
        Guid supplierId,
        GetSupplier getSupplier,
        CancellationToken cancellationToken)
    {
        var fornecedor = await getSupplier.ExecuteAsync(supplierId, cancellationToken);

        return fornecedor is null
            ? Results.NotFound(new { erro = "Fornecedor não encontrado." })
            : Results.Ok(fornecedor);
    }

    private static async Task<IResult> RegisterSupplierAsync(
        RegisterSupplierRequest request,
        RegisterSupplier registerSupplier,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await registerSupplier.ExecuteAsync(
            request.Name,
            request.TaxId,
            request.Iban,
            request.Email,
            request.Phone,
            BuildAuditContext(http),
            cancellationToken);

        return resultado.Outcome switch
        {
            RegisterSupplierOutcome.Registered =>
                Results.Created($"/procurement/suppliers/{resultado.SupplierId}",
                    new { supplierId = resultado.SupplierId }),

            RegisterSupplierOutcome.DuplicateTaxId =>
                Results.Conflict(new
                {
                    erro = "Já existe um fornecedor com este NIF.",
                    supplierId = resultado.SupplierId,
                }),

            RegisterSupplierOutcome.Rejected =>
                Results.BadRequest(new { erro = resultado.Error }),

            _ => Results.Problem("Resultado inesperado ao registar o fornecedor."),
        };
    }

    private static async Task<IResult> UpdateSupplierAsync(
        Guid supplierId,
        UpdateSupplierRequest request,
        UpdateSupplier updateSupplier,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await updateSupplier.ExecuteAsync(
            supplierId,
            request.Name,
            request.Iban,
            request.Email,
            request.Phone,
            BuildAuditContext(http),
            cancellationToken);

        return resultado.Outcome switch
        {
            UpdateSupplierOutcome.Updated => Results.NoContent(),
            UpdateSupplierOutcome.NotFound => Results.NotFound(new { erro = "Fornecedor não encontrado." }),
            UpdateSupplierOutcome.Rejected => Results.BadRequest(new { erro = resultado.Error }),
            _ => Results.Problem("Resultado inesperado ao actualizar o fornecedor."),
        };
    }

    private static async Task<IResult> SetSupplierStatusAsync(
        Guid supplierId,
        SetSupplierStatusRequest request,
        SetSupplierStatus setStatus,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var encontrado = await setStatus.ExecuteAsync(
            supplierId, request.Active, BuildAuditContext(http), cancellationToken);

        return encontrado
            ? Results.NoContent()
            : Results.NotFound(new { erro = "Fornecedor não encontrado." });
    }

    private static async Task<IResult> ListRequisitionsAsync(
        ListRequisitions listRequisitions,
        Guid? requestedByEmployeeId,
        string? status,
        CancellationToken cancellationToken) =>
        Results.Ok(await listRequisitions.ExecuteAsync(
            requestedByEmployeeId, status, cancellationToken));

    private static async Task<IResult> GetRequisitionAsync(
        Guid requisitionId,
        GetRequisition getRequisition,
        CancellationToken cancellationToken)
    {
        var requisicao = await getRequisition.ExecuteAsync(requisitionId, cancellationToken);

        return requisicao is null
            ? Results.NotFound(new { erro = "Requisição não encontrada." })
            : Results.Ok(requisicao);
    }

    private static async Task<IResult> OpenRequisitionAsync(
        OpenRequisitionRequest request,
        OpenRequisition openRequisition,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var linhas = request.Lines?
            .Select(l => new NewRequisitionLine(l.Description, l.Quantity, l.EstimatedUnitPrice))
            .ToList() ?? [];

        var resultado = await openRequisition.ExecuteAsync(
            request.RequestedByEmployeeId,
            request.DepartmentId,
            request.Justification,
            request.Currency ?? "AOA",
            request.RequestedOn,
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return resultado.Outcome switch
        {
            OpenRequisitionOutcome.Opened =>
                Results.Created($"/procurement/requisitions/{resultado.RequisitionId}",
                    new
                    {
                        requisitionId = resultado.RequisitionId,
                        estimatedTotal = resultado.EstimatedTotal,
                        estado = "Draft",
                    }),

            OpenRequisitionOutcome.RequesterNotFound =>
                Results.NotFound(new { erro = resultado.Error }),

            OpenRequisitionOutcome.Rejected =>
                Results.BadRequest(new { erro = resultado.Error }),

            _ => Results.Problem("Resultado inesperado ao abrir a requisição."),
        };
    }

    private static async Task<IResult> SubmitRequisitionAsync(
        Guid requisitionId,
        SubmitRequisition submitRequisition,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await submitRequisition.ExecuteAsync(
            requisitionId, BuildAuditContext(http), cancellationToken);

        return resultado.Outcome switch
        {
            SubmitRequisitionOutcome.Submitted =>
                Results.Accepted(
                    $"/approval/requests/{resultado.ApprovalRequestId}",
                    new
                    {
                        approvalRequestId = resultado.ApprovalRequestId,
                        estado = "PendingApproval",
                    }),

            SubmitRequisitionOutcome.NotFound =>
                Results.NotFound(new { erro = resultado.Error }),

            // 501 e não 409: a capacidade não está ligada neste ambiente. Mesma
            // tradução que `hr` faz para o mesmo caso.
            SubmitRequisitionOutcome.ApprovalUnavailable =>
                Results.Problem(resultado.Error, statusCode: StatusCodes.Status501NotImplemented),

            SubmitRequisitionOutcome.SubmissionFailed or SubmitRequisitionOutcome.Rejected =>
                Results.Conflict(new { erro = resultado.Error }),

            _ => Results.Problem("Resultado inesperado ao submeter a requisição."),
        };
    }

    private static async Task<IResult> ApplyDecisionAsync(
        Guid requisitionId,
        ApplyRequisitionDecision applyDecision,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await applyDecision.ExecuteAsync(
            requisitionId, BuildAuditContext(http), cancellationToken);

        return resultado.Outcome switch
        {
            RequisitionDecisionOutcome.Applied or RequisitionDecisionOutcome.AlreadySettled =>
                Results.Ok(new { estado = resultado.Status }),

            // 202 e não 200: ainda está em curso, e quem chamou tem de voltar.
            RequisitionDecisionOutcome.StillPending =>
                Results.Accepted(value: new { estado = resultado.Status }),

            RequisitionDecisionOutcome.NotFound =>
                Results.NotFound(new { erro = resultado.Error }),

            _ => Results.Problem("Resultado inesperado ao aplicar a decisão."),
        };
    }

    private static async Task<IResult> CancelRequisitionAsync(
        Guid requisitionId,
        CancelRequisitionRequest request,
        CancelRequisition cancelRequisition,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await cancelRequisition.ExecuteAsync(
            requisitionId, request.Reason, BuildAuditContext(http), cancellationToken);

        return resultado.Outcome switch
        {
            CancelRequisitionOutcome.Cancelled => Results.NoContent(),
            CancelRequisitionOutcome.NotFound => Results.NotFound(new { erro = resultado.Error }),
            CancelRequisitionOutcome.Rejected => Results.Conflict(new { erro = resultado.Error }),
            _ => Results.Problem("Resultado inesperado ao cancelar a requisição."),
        };
    }

    private static async Task<IResult> ListOrdersAsync(
        ListPurchaseOrders listOrders,
        Guid? requisitionId,
        Guid? supplierId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listOrders.ExecuteAsync(requisitionId, supplierId, cancellationToken));

    private static async Task<IResult> GetOrderAsync(
        Guid purchaseOrderId,
        GetPurchaseOrder getOrder,
        CancellationToken cancellationToken)
    {
        var ordem = await getOrder.ExecuteAsync(purchaseOrderId, cancellationToken);

        return ordem is null
            ? Results.NotFound(new { erro = "Ordem de compra não encontrada." })
            : Results.Ok(ordem);
    }

    private static async Task<IResult> IssueOrderAsync(
        Guid requisitionId,
        IssuePurchaseOrderRequest request,
        IssuePurchaseOrder issueOrder,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var linhas = request.Lines?
            .Select(l => new NewPurchaseOrderLine(l.Description, l.Quantity, l.UnitPrice))
            .ToList() ?? [];

        var resultado = await issueOrder.ExecuteAsync(
            requisitionId,
            request.SupplierId,
            request.IssuedOn,
            request.ExpectedOn,
            linhas,
            BuildAuditContext(http),
            cancellationToken);

        return resultado.Outcome switch
        {
            IssuePurchaseOrderOutcome.Issued =>
                Results.Created($"/procurement/orders/{resultado.PurchaseOrderId}",
                    new
                    {
                        purchaseOrderId = resultado.PurchaseOrderId,
                        total = resultado.Total,
                        estado = "Issued",
                    }),

            IssuePurchaseOrderOutcome.RequisitionNotFound or IssuePurchaseOrderOutcome.SupplierNotFound =>
                Results.NotFound(new { erro = resultado.Error }),

            // 409 nos três: é o estado que impede, e não o corpo do pedido.
            // `ExceedsApproved` em particular não é um pedido inválido — é
            // alçada esgotada, e corrige-se com uma requisição nova.
            IssuePurchaseOrderOutcome.RequisitionNotApproved
                or IssuePurchaseOrderOutcome.SupplierInactive
                or IssuePurchaseOrderOutcome.ExceedsApproved =>
                Results.Conflict(new { erro = resultado.Error }),

            IssuePurchaseOrderOutcome.Rejected =>
                Results.BadRequest(new { erro = resultado.Error }),

            _ => Results.Problem("Resultado inesperado ao emitir a ordem de compra."),
        };
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid purchaseOrderId,
        CancelPurchaseOrderRequest request,
        CancelPurchaseOrder cancelOrder,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var resultado = await cancelOrder.ExecuteAsync(
            purchaseOrderId, request.Reason, BuildAuditContext(http), cancellationToken);

        return resultado.Outcome switch
        {
            CancelPurchaseOrderOutcome.Cancelled => Results.NoContent(),
            CancelPurchaseOrderOutcome.NotFound => Results.NotFound(new { erro = resultado.Error }),
            CancelPurchaseOrderOutcome.Rejected => Results.Conflict(new { erro = resultado.Error }),
            _ => Results.Problem("Resultado inesperado ao cancelar a ordem de compra."),
        };
    }
    /// <summary>
    /// Constrói o contexto de auditoria a partir do pedido. É a camada API que
    /// conhece o transporte; as de baixo recebem-no já feito.
    /// </summary>
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

/// <param name="Iban">
/// Opcional. Verificado pela norma ISO 13616 — um IBAN com um dígito trocado é
/// recusado com `400`, e não guardado à espera de pagar a outra pessoa.
/// </param>
public sealed record RegisterSupplierRequest(
    string Name,
    string TaxId,
    string? Iban,
    string? Email,
    string? Phone);

/// <param name="Iban">
/// Omitido não apaga o IBAN existente — só o substitui quando vem preenchido.
/// Para o apagar, enviar cadeia vazia.
/// </param>
public sealed record UpdateSupplierRequest(
    string? Name,
    string? Iban,
    string? Email,
    string? Phone);

public sealed record SetSupplierStatusRequest(bool Active);

/// <param name="Currency">ISO 4217. Omitido, assume-se `AOA`.</param>
/// <param name="DepartmentId">
/// Omitido, usa-se o departamento do requisitante. É ele que escolhe a política
/// de aprovação aplicável.
/// </param>
public sealed record OpenRequisitionRequest(
    Guid RequestedByEmployeeId,
    Guid? DepartmentId,
    string Justification,
    string? Currency,
    DateOnly? RequestedOn,
    IReadOnlyList<RequisitionLineRequest>? Lines);

public sealed record RequisitionLineRequest(
    string Description,
    decimal Quantity,
    decimal EstimatedUnitPrice);

public sealed record CancelRequisitionRequest(string Reason);

/// <param name="IssuedOn">Omitido, assume-se hoje.</param>
/// <param name="ExpectedOn">
/// Entrega esperada. Opcional — nem sempre está acordada — e nunca anterior à
/// emissão.
/// </param>
public sealed record IssuePurchaseOrderRequest(
    Guid SupplierId,
    DateOnly? IssuedOn,
    DateOnly? ExpectedOn,
    IReadOnlyList<PurchaseOrderLineRequest>? Lines);

/// <param name="UnitPrice">
/// Preço <strong>acordado</strong> com o fornecedor, e não o estimado na
/// requisição.
/// </param>
public sealed record PurchaseOrderLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice);

public sealed record CancelPurchaseOrderRequest(string Reason);
