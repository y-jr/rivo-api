using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Payroll.Application.UseCases;
using Rivo.Payroll.Contracts;

namespace Rivo.Payroll.Api;

public static class PayrollModuleEndpoints
{
    public static IEndpointRouteBuilder MapPayrollModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/payroll");

        group.MapGet("/runs", ListAsync)
            .RequireAuthorization(PayrollPermissions.RunsRead);

        group.MapGet("/runs/{runId:guid}", GetAsync)
            .RequireAuthorization(PayrollPermissions.RunsRead);

        group.MapPost("/runs", OpenAsync)
            .RequireAuthorization(PayrollPermissions.RunsWrite);

        group.MapPost("/runs/{runId:guid}/items", AddItemAsync)
            .RequireAuthorization(PayrollPermissions.RunsWrite);

        group.MapPost("/runs/{runId:guid}/submission", SubmitAsync)
            .RequireAuthorization(PayrollPermissions.RunsWrite);

        // Aplica a decisão de `approval`, se já houver uma. `payroll` pergunta;
        // `approval` nunca empurra — mesmo padrão de `procurement`.
        group.MapPost("/runs/{runId:guid}/decision", ApplyDecisionAsync)
            .RequireAuthorization(PayrollPermissions.RunsRead);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListPayrollRuns listRuns, CancellationToken cancellationToken) =>
        Results.Ok(await listRuns.ExecuteAsync(cancellationToken));

    private static async Task<IResult> GetAsync(
        Guid runId, GetPayrollRun getRun, CancellationToken cancellationToken)
    {
        var folha = await getRun.ExecuteAsync(runId, cancellationToken);

        return folha is null
            ? Results.NotFound(new { erro = "Folha não encontrada." })
            : Results.Ok(folha);
    }

    private static async Task<IResult> OpenAsync(
        OpenRunRequest request,
        OpenPayrollRun openRun,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var runId = await openRun.ExecuteAsync(
            request.Year, request.Month, request.OpenedByEmployeeId,
            BuildAuditContext(http), cancellationToken);

        return Results.Created($"/payroll/runs/{runId}", new { runId });
    }

    private static async Task<IResult> AddItemAsync(
        Guid runId,
        AddItemRequest request,
        AddPayrollItem addItem,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var outcome = await addItem.ExecuteAsync(
            runId, request.EmployeeId, request.GrossSalary, BuildAuditContext(http), cancellationToken);

        return outcome switch
        {
            AddItemOutcome.Added => Results.NoContent(),
            AddItemOutcome.NotFound => Results.NotFound(new { erro = "Folha não encontrada." }),
            AddItemOutcome.Rejected => Results.Conflict(new { erro = "Não foi possível acrescentar o item." }),
            _ => Results.Problem("Resultado inesperado ao acrescentar o item."),
        };
    }

    private static async Task<IResult> SubmitAsync(
        Guid runId,
        SubmitPayrollRun submit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await submit.ExecuteAsync(runId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            SubmitRunOutcome.Submitted => Results.Ok(new { approvalRequestId = result.ApprovalRequestId }),
            SubmitRunOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 501: o motor de governança não está ligado neste ambiente — não
            // é a folha que está errada, é a capacidade que falta.
            SubmitRunOutcome.ApprovalUnavailable =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status501NotImplemented),

            SubmitRunOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            SubmitRunOutcome.SubmissionFailed => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao submeter a folha."),
        };
    }

    private static async Task<IResult> ApplyDecisionAsync(
        Guid runId,
        ApplyPayrollDecision applyDecision,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await applyDecision.ExecuteAsync(runId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ApplyDecisionOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            _ => Results.Ok(new { status = result.Status }),
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

public sealed record OpenRunRequest(int Year, int Month, Guid OpenedByEmployeeId);

public sealed record AddItemRequest(Guid EmployeeId, decimal GrossSalary);
