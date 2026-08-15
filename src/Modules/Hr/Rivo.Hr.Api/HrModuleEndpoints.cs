using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;

namespace Rivo.Hr.Api;

public static class HrModuleEndpoints
{
    public static IEndpointRouteBuilder MapHrModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/hr");

        group.MapGet("/employees", ListEmployeesAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead);

        group.MapPost("/employees", HireEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite);

        group.MapGet("/employees/{employeeId:guid}", GetEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead);

        group.MapGet("/departments", ListDepartmentsAsync)
            .RequireAuthorization(HrPermissions.DepartmentsRead);

        group.MapPost("/departments", CreateDepartmentAsync)
            .RequireAuthorization(HrPermissions.DepartmentsWrite);

        group.MapGet("/positions", ListPositionsAsync)
            .RequireAuthorization(HrPermissions.PositionsRead);

        // Catálogo de Cargos: só Admin. Quem controla a marca de autoridade
        // controla, indirectamente, quem pode vir a aprovar (ADR-015).
        group.MapPost("/positions", CreatePositionAsync)
            .RequireAuthorization(HrPermissions.PositionsWrite);

        // Atribuição: operação corrente de RH.
        group.MapPost("/employees/{employeeId:guid}/positions", AssignPositionAsync)
            .RequireAuthorization(HrPermissions.PositionsAssign);

        // Anexar exige permissão de escrita em colaboradores, não de
        // documentos: está a alterar-se o registo do colaborador. O upload do
        // ficheiro é que exige `documents.write`.
        group.MapPost("/employees/{employeeId:guid}/documents", AttachDocumentAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite);

        group.MapGet("/employees/{employeeId:guid}/documents", ListEmployeeDocumentsAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead);

        return endpoints;
    }

    private static async Task<IResult> AttachDocumentAsync(
        Guid employeeId,
        AttachDocumentRequest request,
        AttachDocumentToEmployee attach,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await attach.ExecuteAsync(
            employeeId, request.DocumentId, request.Category, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            AttachDocumentOutcome.Attached =>
                Results.Created($"/hr/employees/{employeeId}/documents", new { linkId = result.LinkId }),
            _ => Results.NotFound(new { erro = result.Message }),
        };
    }

    private static async Task<IResult> ListEmployeeDocumentsAsync(
        Guid employeeId,
        ListEmployeeDocuments list,
        CancellationToken cancellationToken) =>
        Results.Ok(await list.ExecuteAsync(employeeId, cancellationToken));

    private static AuditContext BuildAuditContext(HttpContext http)
    {
        var actor = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return new AuditContext(
            ActorId: Guid.TryParse(actor, out var id) ? id : null,
            IpAddress: http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId: http.TraceIdentifier);
    }

    private static async Task<IResult> ListEmployeesAsync(
        ListEmployees listEmployees,
        CancellationToken cancellationToken) =>
        Results.Ok(await listEmployees.ExecuteAsync(cancellationToken));

    private static async Task<IResult> GetEmployeeAsync(
        Guid employeeId,
        IEmployeeDirectory directory,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Passa pelo contrato publicado, e não pela persistência: é o mesmo
        // caminho que outros módulos usarão (ADR-010).
        var reference = await directory.FindAsync(employeeId, clock.GetUtcNow(), cancellationToken);

        return reference is null ? Results.NotFound() : Results.Ok(reference);
    }

    private static async Task<IResult> HireEmployeeAsync(
        HireEmployeeRequest request,
        HireEmployee hireEmployee,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await hireEmployee.ExecuteAsync(
            request.FullName,
            request.DepartmentId,
            request.UserId,
            request.HiredOn ?? clock.GetUtcNow(),
            BuildAuditContext(http),
            cancellationToken);

        return result.Succeeded
            ? Results.Created($"/hr/employees/{result.EmployeeId}", new { employeeId = result.EmployeeId })
            : Results.NotFound(new { erro = result.Error });
    }

    private static async Task<IResult> ListDepartmentsAsync(
        ListDepartments listDepartments,
        CancellationToken cancellationToken) =>
        Results.Ok(await listDepartments.ExecuteAsync(cancellationToken));

    private static async Task<IResult> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CreateDepartment createDepartment,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var id = await createDepartment.ExecuteAsync(
            request.Name, request.ManagerId, BuildAuditContext(http), cancellationToken);

        return Results.Created($"/hr/departments/{id}", new { departmentId = id });
    }

    private static async Task<IResult> ListPositionsAsync(
        ListPositions listPositions,
        CancellationToken cancellationToken) =>
        Results.Ok(await listPositions.ExecuteAsync(cancellationToken));

    private static async Task<IResult> CreatePositionAsync(
        CreatePositionRequest request,
        CreatePosition createPosition,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var id = await createPosition.ExecuteAsync(
            request.Name,
            request.HierarchyLevel,
            request.GrantsApprovalAuthority,
            BuildAuditContext(http),
            cancellationToken);

        return Results.Created($"/hr/positions/{id}", new { positionId = id });
    }

    private static async Task<IResult> AssignPositionAsync(
        Guid employeeId,
        AssignPositionRequest request,
        AssignPosition assignPosition,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await assignPosition.ExecuteAsync(
            employeeId,
            request.PositionId,
            request.EffectiveFrom ?? clock.GetUtcNow(),
            request.EffectiveTo,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            AssignPositionOutcome.Assigned =>
                Results.Created($"/hr/employees/{employeeId}", new { assignmentId = result.AssignmentId }),

            AssignPositionOutcome.EmployeeNotFound or AssignPositionOutcome.PositionNotFound =>
                Results.NotFound(new { erro = result.Message }),

            // 501: a regra existe e é conhecida, mas falta a capacidade do
            // sistema para a satisfazer. Não é erro do chamador (4xx) nem
            // falha inesperada (500).
            AssignPositionOutcome.RequiresApproval =>
                Results.Problem(result.Message, statusCode: StatusCodes.Status501NotImplemented),

            _ => Results.Problem("Resultado inesperado ao atribuir o cargo."),
        };
    }
}

// DTOs da fronteira HTTP. Entidades de domínio nunca são expostas.
public sealed record HireEmployeeRequest(string FullName, Guid? DepartmentId, Guid? UserId, DateTimeOffset? HiredOn);

public sealed record CreateDepartmentRequest(string Name, Guid? ManagerId);

public sealed record CreatePositionRequest(string Name, int HierarchyLevel, bool GrantsApprovalAuthority);

public sealed record AssignPositionRequest(Guid PositionId, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <param name="Category">Classificação em RH: "contrato", "declaracao", "cv".</param>
public sealed record AttachDocumentRequest(Guid DocumentId, string Category);
