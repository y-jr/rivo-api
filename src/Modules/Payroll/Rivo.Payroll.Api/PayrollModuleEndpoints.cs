using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Payroll.Application.UseCases;
using Rivo.Payroll.Contracts;
using Rivo.Payroll.Domain;

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

        // Anexar exige permissão de escrita em folhas, não de documentos: está
        // a alterar-se o registo do item. O upload do ficheiro é que exige
        // `documents.write` — mesma separação de `hr`.
        group.MapPost("/runs/{runId:guid}/items/{itemId:guid}/documents", AttachDocumentAsync)
            .RequireAuthorization(PayrollPermissions.RunsWrite);

        group.MapGet("/runs/{runId:guid}/items/{itemId:guid}/documents", ListItemDocumentsAsync)
            .RequireAuthorization(PayrollPermissions.RunsRead);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ListPayrollRuns listRuns, CancellationToken cancellationToken)
    {
        var folhas = await listRuns.ExecuteAsync(cancellationToken);
        return Results.Ok(folhas.Select(ToView));
    }

    private static async Task<IResult> GetAsync(
        Guid runId, GetPayrollRun getRun, CancellationToken cancellationToken)
    {
        var folha = await getRun.ExecuteAsync(runId, cancellationToken);

        return folha is null
            ? Results.NotFound(new { erro = "Folha não encontrada." })
            : Results.Ok(ToView(folha));
    }

    // A entidade de domínio nunca é exposta como modelo de transporte
    // (architecture/dependency-rules.md) — sem isto, Status sairia como o
    // inteiro subjacente do enum, e não como "Draft"/"PendingApproval".
    private static PayrollRunView ToView(PayrollRun folha) => new(
        folha.Id, folha.Year, folha.Month, folha.Status.ToString(),
        folha.ApprovalRequestId, folha.SubmittedAt, folha.ClosedAt,
        [.. folha.Items.Select(ToView)]);

    private static PayrollItemView ToView(PayrollItem item) => new(
        item.Id, item.EmployeeId, item.GrossSalary,
        item.NetSalary, item.WithholdingTax, item.SocialSecurityContribution);

    private static async Task<IResult> OpenAsync(
        OpenRunRequest request,
        OpenPayrollRun openRun,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openRun.ExecuteAsync(
            request.Year, request.Month, request.OpenedByEmployeeId,
            BuildAuditContext(http), cancellationToken);

        return result.Succeeded
            ? Results.Created($"/payroll/runs/{result.RunId}", new { runId = result.RunId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["folha"] = [result.Error!] });
    }

    private static async Task<IResult> AddItemAsync(
        Guid runId,
        AddItemRequest request,
        AddPayrollItem addItem,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await addItem.ExecuteAsync(
            runId, request.EmployeeId, request.GrossSalary, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AddItemResultKind.Added => Results.Created($"/payroll/runs/{runId}", new { itemId = result.ItemId }),

            AddItemResultKind.NotFound => Results.NotFound(new { erro = result.Error }),

            // 400: campo mal preenchido (salário não positivo) ou falta de
            // configuração fiscal — em ambos os casos o pedido corrige-se do
            // lado do chamador, não é conflito com o estado da folha.
            AddItemResultKind.Rejected or AddItemResultKind.FiscalDataMissing =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["item"] = [result.Error!] }),

            // 409: a folha já não está em rascunho.
            AddItemResultKind.Conflict =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

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

    private static async Task<IResult> AttachDocumentAsync(
        Guid runId,
        Guid itemId,
        AttachPayrollDocumentRequest request,
        AttachDocumentToPayrollItem attach,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await attach.ExecuteAsync(
            runId, itemId, request.DocumentId, request.Category, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AttachPayrollDocumentOutcome.Attached => Results.Created(
                $"/payroll/runs/{runId}/items/{itemId}/documents", new { linkId = result.LinkId }),

            AttachPayrollDocumentOutcome.RunNotFound or AttachPayrollDocumentOutcome.ItemNotFound
                or AttachPayrollDocumentOutcome.DocumentNotFound =>
                Results.NotFound(new { erro = result.Error }),

            // 400: categoria em branco — campo mal preenchido.
            AttachPayrollDocumentOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["documento"] = [result.Error!] }),

            // 409: a folha ainda não está Aprovada.
            AttachPayrollDocumentOutcome.Conflict =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),

            _ => Results.Problem("Resultado inesperado ao anexar o documento."),
        };
    }

    private static async Task<IResult> ListItemDocumentsAsync(
        Guid itemId,
        ListPayrollItemDocuments list,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(itemId, cancellationToken));

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

public sealed record AttachPayrollDocumentRequest(Guid DocumentId, string Category);

public sealed record PayrollRunView(
    Guid RunId,
    int Year,
    int Month,
    string Status,
    Guid? ApprovalRequestId,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<PayrollItemView> Items);

public sealed record PayrollItemView(
    Guid ItemId,
    Guid EmployeeId,
    decimal GrossSalary,
    decimal? NetSalary,
    decimal? WithholdingTax,
    decimal? SocialSecurityContribution);
