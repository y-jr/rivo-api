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

        // Desactivar, nunca eliminar: os pedidos em curso guardam a política
        // que lhes foi aplicada (BR-6), e apagá-la deixava-os a apontar para o
        // nada.
        group.MapPost("/policies/{policyId:guid}/deactivation", DeactivatePolicyAsync)
            .RequireAuthorization(ApprovalPermissions.PoliciesWrite);

        group.MapGet("/requests", ListRequestsAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsRead);

        group.MapGet("/requests/{requestId:guid}", GetRequestAsync)
            .RequireAuthorization(ApprovalPermissions.RequestsRead);

        // A linha do tempo completa, para quem reconstroi o que aconteceu — e
        // nao so quem espera pela sua vez de decidir.
        group.MapGet("/requests/{requestId:guid}/history", GetHistoryAsync)
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


    private static async Task<IResult> DeactivatePolicyAsync(
        Guid policyId,
        DeactivateApprovalPolicy deactivatePolicy,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await deactivatePolicy.ExecuteAsync(
            policyId, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            // Já desactivada devolve o mesmo que desactivar agora: o estado
            // pretendido verifica-se nos dois casos, e quem chama repete sem
            // ter de distinguir.
            DeactivatePolicyOutcome.Deactivated or DeactivatePolicyOutcome.AlreadyInactive =>
                Results.NoContent(),

            DeactivatePolicyOutcome.NotFound =>
                Results.NotFound(new { erro = "Política de aprovação não encontrada." }),

            _ => Results.Problem("Resultado inesperado ao desactivar a política."),
        };
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

    private static async Task<IResult> GetHistoryAsync(
        Guid requestId,
        GetApprovalRequestHistory getHistory,
        CancellationToken cancellationToken)
    {
        var historico = await getHistory.ExecuteAsync(requestId, cancellationToken);

        return historico is null ? Results.NotFound() : Results.Ok(historico);
    }

    private static async Task<IResult> DecideAsync(
        Guid requestId,
        DecideRequest request,
        DecideOnRequest decide,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var contexto = BuildAuditContext(http);

        // Sem identificador de conta no token não há decisor possível. Não é
        // 401 — o token é válido; é 403, porque falta o vínculo (ADR-050).
        if (contexto.ActorId is not { } quemDecide)
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await decide.ExecuteAsync(
            requestId,
            quemDecide,
            request.Action,
            request.Notes,
            contexto,
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
        var contexto = BuildAuditContext(http);

        if (contexto.ActorId is not { } quemCancela)
        {
            return Results.Problem(
                "Sessão sem identificador de utilizador.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await cancel.ExecuteAsync(
            requestId, quemCancela, contexto, cancellationToken);

        return result.Outcome switch
        {
            DecisionOutcome.Recorded => Results.NoContent(),
            DecisionOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 403 e não 409, pela mesma razão da decisão: não é o estado do
            // pedido que impede, é esta pessoa que não pode cancelá-lo (K18).
            DecisionOutcome.SegregationViolation =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status403Forbidden),

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

/// <summary>
/// <strong>Quem decide não vem daqui.</strong> Resolve-se da conta
/// autenticada, em <c>DecideOnRequest</c> (ADR-050).
///
/// <para>
/// Até 2026-09-04 este corpo tinha um <c>DecidedByEmployeeId</c>, com a
/// justificação de que «nem todo o utilizador é colaborador (ADR-004)». O
/// facto estava certo e a conclusão errada: a resposta a essa verdade é
/// **resolver o colaborador a partir da conta e recusar quando não há
/// vínculo**, não deixar quem chama declarar quem é. Enquanto foi assim,
/// BR-2 e BR-4 eram verificadas contra o colaborador declarado e não contra
/// o autor — e quem tivesse <c>approval.requests.decide</c> aprovava o seu
/// próprio pedido indicando outra pessoa.
/// </para>
/// </summary>
/// <param name="Action">Approved, Rejected ou ClarificationRequested.</param>
public sealed record DecideRequest(string Action, string? Notes);
