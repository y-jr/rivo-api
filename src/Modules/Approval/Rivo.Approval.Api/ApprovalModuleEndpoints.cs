using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Approval.Application.UseCases;
using Rivo.Approval.Contracts;
using Rivo.Audit.Contracts;

namespace Rivo.Approval.Api;

public static class ApprovalModuleEndpoints
{
    public static IEndpointRouteBuilder MapApprovalModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/approval");

        // Políticas: configuração sensível — altera quem aprova o quê.
        group.MapGet("/policies", ListPoliciesAsync)
            .RequireAuthorization(ApprovalPermissions.PoliciesRead);

        group.MapPost("/policies", CreatePolicyAsync)
            .RequireAuthorization(ApprovalPermissions.PoliciesWrite);

        group.MapGet("/requests", ListRequestsAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsRead);

        group.MapGet("/requests/{requestId:guid}", GetRequestAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsRead);

        // Decidir. A permissão abre a porta; quem decide de facto é o domínio,
        // que verifica BR-2, BR-4 e a atribuição ao passo em curso.
        group.MapPost("/requests/{requestId:guid}/decisions", DecideAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsDecide);

        group.MapPost("/requests/{requestId:guid}/cancellation", CancelAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsRead);

        return endpoints;
    }

    private static async Task<IResult> ListPoliciesAsync(
        ListApprovalPolicies listPolicies,
        CancellationToken cancellationToken) =>
        Results.Ok(await listPolicies.ExecuteAsync(cancellationToken));

    private static async Task<IResult> CreatePolicyAsync(
        CreatePolicyRequest request,
        CreateApprovalPolicy createPolicy,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var steps = (request.Steps ?? [])
            .Select(s => new NewPolicyStep(s.ApproverPositionId, s.Mode ?? "AnyApprover", s.SlaHours))
            .ToList();

        var result = await createPolicy.ExecuteAsync(
            request.ProcessType,
            request.DepartmentId,
            request.MinimumAmount,
            request.MaximumAmount,
            request.RequiresBudgetCheck ?? false,
            steps,
            BuildAuditContext(http),
            cancellationToken);

        return result.Succeeded
            ? Results.Created("/approval/policies", new { policyId = result.PolicyId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["politica"] = [result.Error!] });
    }

    private static async Task<IResult> ListRequestsAsync(
        ListApprovalRequests listRequests,
        string? processType,
        Guid? pendingFor,
        CancellationToken cancellationToken) =>
        Results.Ok(await listRequests.ExecuteAsync(processType, pendingFor, cancellationToken));

    private static async Task<IResult> GetRequestAsync(
        Guid requestId,
        IApprovalGateway gateway,
        CancellationToken cancellationToken)
    {
        var status = await gateway.GetStatusAsync(requestId, cancellationToken);

        return status is null ? Results.NotFound() : Results.Ok(status);
    }

    private static async Task<IResult> DecideAsync(
        Guid requestId,
        DecideRequest request,
        DecideOnRequest decide,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await decide.ExecuteAsync(
            requestId,
            request.DecidedByEmployeeId,
            request.Action,
            request.Notes,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            DecisionOutcome.Recorded => Results.Ok(result.Status),

            DecisionOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 403 e não 409: não é o estado do pedido que impede, é **esta
            // pessoa** que não pode decidir. BR-2 e BR-4 são regras sobre quem,
            // e a distinção importa para quem investiga a trilha depois.
            DecisionOutcome.SegregationViolation =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status403Forbidden),

            DecisionOutcome.Rejected => Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao registar a decisão."),
        };
    }

    private static async Task<IResult> CancelAsync(
        Guid requestId,
        CancelRequest cancel,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancel.ExecuteAsync(requestId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            DecisionOutcome.Recorded => Results.NoContent(),
            DecisionOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            DecisionOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao cancelar o pedido."),
        };
    }

    /// <summary>
    /// Constrói o contexto de auditoria a partir do pedido. É a camada API que
    /// conhece o transporte.
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

// DTOs da fronteira HTTP. Entidades de domínio nunca são expostas.

/// <param name="MaximumAmount">Exclusivo, para que faixas contíguas não se sobreponham.</param>
public sealed record CreatePolicyRequest(
    string ProcessType,
    Guid? DepartmentId,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    bool? RequiresBudgetCheck,
    IReadOnlyList<PolicyStepRequest>? Steps);

/// <param name="Mode">AnyApprover (omissão) ou AllApprovers.</param>
public sealed record PolicyStepRequest(Guid ApproverPositionId, string? Mode, int? SlaHours);

/// <param name="DecidedByEmployeeId">
/// Quem decide, como Colaborador de `hr`. É contra este identificador que BR-2
/// e BR-4 são verificadas — e não contra o utilizador autenticado, porque nem
/// todo o utilizador é colaborador (ADR-004).
/// </param>
/// <param name="Action">Approved, Rejected ou ClarificationRequested.</param>
public sealed record DecideRequest(Guid DecidedByEmployeeId, string Action, string? Notes);
