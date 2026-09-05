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

        // Liga uma conta de identity a um colaborador já admitido (ADR-051).
        //
        // Permissão própria, e não EmployeesWrite: ao contrário do `commercial`
        // — que usa a permissão de escrita do Cliente para o equivalente
        // (ADR-043) — aqui o vínculo concede o que o Cargo confere. Desde o
        // ADR-050 é ele que determina quem decide aprovações, e portanto quem
        // o cria escolhe, indirectamente, quem aprova.
        group.MapPost("/employees/{employeeId:guid}/account", LinkEmployeeAccountAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount);

        // Desligar (ADR-052). Mesma permissão de ligar: desligar é uma perda
        // de capacidade, não um ganho, e exigir mais do que para ligar
        // atrasaria a correcção de um vínculo errado — que é resposta a
        // incidente, não operação de rotina.
        group.MapDelete("/employees/{employeeId:guid}/account", UnlinkEmployeeAccountAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount);

        // O histórico do vínculo (ADR-053). Mesma permissão de o gerir, e não
        // `hr.employees.read`: expõe o mapa conta↔pessoa ao longo do tempo, que
        // é informação de segurança e não de organograma. Quem só precisa de
        // saber quem trabalha na empresa não precisa de saber com que conta.
        group.MapGet("/employees/{employeeId:guid}/account-history", GetAccountHistoryAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount);

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

        // Aplica a decisão já tomada em governança a uma atribuição pendente.
        //
        // É `hr` que pergunta: `approval` não pode modificar dados de negócio
        // do módulo de origem, por isso a promoção a efectiva parte daqui.
        group.MapPost("/position-assignments/{assignmentId:guid}/approval-outcome", ApplyApprovalOutcomeAsync)
            .RequireAuthorization(HrPermissions.PositionsAssign);

        // Anexar exige permissão de escrita em colaboradores, não de
        // documentos: está a alterar-se o registo do colaborador. O upload do
        // ficheiro é que exige `documents.write`.
        group.MapPost("/employees/{employeeId:guid}/documents", AttachDocumentAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite);

        group.MapGet("/employees/{employeeId:guid}/documents", ListEmployeeDocumentsAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead);

        // Contratos de trabalho. Permissão própria e não `employees.read`: a
        // lista traz a remuneração acordada, que é a informação mais sensível
        // do módulo.
        group.MapGet("/contracts", ListContractsAsync)
            .RequireAuthorization(HrPermissions.ContractsRead);

        group.MapPost("/contracts", DrawContractAsync)
            .RequireAuthorization(HrPermissions.ContractsWrite);

        group.MapPost("/contracts/{contractId:guid}/termination", TerminateContractAsync)
            .RequireAuthorization(HrPermissions.ContractsWrite);

        // Assiduidade.
        group.MapGet("/attendance", ListAttendanceAsync)
            .RequireAuthorization(HrPermissions.AttendanceRead);

        // Marcação de ponto: uma rota, porque é um botão só. Entrada ou saída,
        // decide o servidor consoante o dia já esteja aberto.
        group.MapPost("/attendance/clock", ClockAsync)
            .RequireAuthorization(HrPermissions.AttendanceWrite);

        group.MapPost("/attendance/absences", RecordAbsenceAsync)
            .RequireAuthorization(HrPermissions.AttendanceWrite);

        // Férias e outras ausências planeadas. Passam por governança: um pedido
        // pendente **não é ausência** (mesmo princípio de BR-20).
        group.MapGet("/leave", ListLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveRead);

        group.MapPost("/leave", RequestLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite);

        group.MapPost("/leave/{leaveId:guid}/cancellation", CancelLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite);

        group.MapPost("/leave/{leaveId:guid}/approval-outcome", ApplyLeaveOutcomeAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite);

        // Benefícios: catálogo e adesões.
        group.MapGet("/benefits", ListBenefitsAsync)
            .RequireAuthorization(HrPermissions.BenefitsRead);

        group.MapPost("/benefits", CreateBenefitAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite);

        group.MapGet("/benefits/enrolments", ListEnrolmentsAsync)
            .RequireAuthorization(HrPermissions.BenefitsRead);

        group.MapPost("/benefits/enrolments", EnrolAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite);

        group.MapPost("/benefits/enrolments/{enrolmentId:guid}/cancellation", CancelEnrolmentAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite);

        // Recrutamento: vagas e funil de candidatos.
        group.MapGet("/recruitment/openings", ListOpeningsAsync)
            .RequireAuthorization(HrPermissions.RecruitmentRead);

        group.MapPost("/recruitment/openings", OpenOpeningAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite);

        group.MapPost("/recruitment/openings/{openingId:guid}/closure", CloseOpeningAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite);

        group.MapGet("/recruitment/candidates", ListCandidatesAsync)
            .RequireAuthorization(HrPermissions.RecruitmentRead);

        group.MapPost("/recruitment/openings/{openingId:guid}/candidates", ApplyAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite);

        group.MapPost("/recruitment/candidates/{candidateId:guid}/stage", AdvanceCandidateAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite);

        // Contratar cria um Colaborador — por isso exige também escrita em
        // colaboradores, e não só em recrutamento.
        group.MapPost("/recruitment/candidates/{candidateId:guid}/hire", HireCandidateAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite);

        // Entrada e saída, conduzidas por checklist.
        group.MapGet("/lifecycle", ListLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleRead);

        group.MapPost("/lifecycle", StartLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite);

        group.MapPost("/lifecycle/{processId:guid}/tasks/{taskId:guid}/completion", CompleteTaskAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite);

        group.MapPost("/lifecycle/{processId:guid}/completion", CompleteLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite);

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

        return result.Outcome switch
        {
            HireEmployeeOutcome.Hired =>
                Results.Created($"/hr/employees/{result.EmployeeId}", new { employeeId = result.EmployeeId }),
            HireEmployeeOutcome.DepartmentNotFound => Results.NotFound(new { erro = result.Error }),
            HireEmployeeOutcome.UserAlreadyLinked => Results.Conflict(new { erro = result.Error }),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> LinkEmployeeAccountAsync(
        Guid employeeId,
        LinkEmployeeAccountRequest request,
        LinkEmployeeAccount linkAccount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await linkAccount.ExecuteAsync(
            employeeId, request.UserId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            LinkEmployeeAccountOutcome.Linked => Results.NoContent(),

            LinkEmployeeAccountOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 409 nos dois sentidos do conflito: a conta já é de outra pessoa,
            // ou esta pessoa já tem outra conta. O pedido está bem formado —
            // colide com o estado.
            LinkEmployeeAccountOutcome.UserAlreadyLinked or
            LinkEmployeeAccountOutcome.EmployeeAlreadyLinked =>
                Results.Conflict(new { erro = result.Error }),

            // 403 e não 409: não é o estado que impede, é **quem está a
            // pedir**. Mesma distinção que `approval` faz para BR-2 e BR-4.
            LinkEmployeeAccountOutcome.SelfLinkRefused =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status403Forbidden),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> UnlinkEmployeeAccountAsync(
        Guid employeeId,
        UnlinkEmployeeAccount unlinkAccount,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await unlinkAccount.ExecuteAsync(
            employeeId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            // Desligar quem já está desligado devolve o mesmo: o estado
            // pretendido verifica-se nos dois casos.
            UnlinkEmployeeAccountOutcome.Unlinked => Results.NoContent(),

            UnlinkEmployeeAccountOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Desfecho sem tradução HTTP."),
        };
    }

    private static async Task<IResult> GetAccountHistoryAsync(
        Guid employeeId,
        GetEmployeeAccountHistory getHistory,
        CancellationToken cancellationToken)
    {
        var historico = await getHistory.ExecuteAsync(employeeId, cancellationToken);

        // Lista vazia e 404 dizem coisas diferentes: "nunca teve conta" e "não
        // há tal pessoa".
        return historico is null
            ? Results.NotFound(new { erro = "Colaborador não encontrado." })
            : Results.Ok(historico);
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

            // 202: aceite, mas **sem efeito ainda** (BR-20). A distinção face
            // ao 201 é o ponto — o cargo não foi atribuído, foi submetido, e
            // não confere autoridade nenhuma enquanto não for aprovado.
            AssignPositionOutcome.PendingApproval =>
                Results.Accepted(
                    $"/hr/employees/{employeeId}",
                    new { assignmentId = result.AssignmentId, estado = "PendenteAprovacao", detalhe = result.Message }),

            // 501: a regra existe e é conhecida, mas não há motor de governança
            // ligado neste ambiente. Não é erro do chamador (4xx) nem falha
            // inesperada (500).
            AssignPositionOutcome.ApprovalUnavailable =>
                Results.Problem(result.Message, statusCode: StatusCodes.Status501NotImplemented),

            // 409: a governança recebeu e recusou — política em falta, ambígua,
            // ou nenhum cargo dela com ocupante. É configuração por corrigir.
            AssignPositionOutcome.ApprovalRefusedSubmission =>
                Results.Conflict(new { erro = result.Message }),

            _ => Results.Problem("Resultado inesperado ao atribuir o cargo."),
        };
    }

    private static async Task<IResult> ApplyApprovalOutcomeAsync(
        Guid assignmentId,
        ApplyPositionApprovalOutcome apply,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await apply.ExecuteAsync(assignmentId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ApplyApprovalOutcome.Applied =>
                Results.Ok(new { estado = result.Status }),

            // 200 e não erro: chamar duas vezes é suposto ser inofensivo, e é
            // isso que permite chamá-la sem coordenação.
            ApplyApprovalOutcome.AlreadyResolved =>
                Results.Ok(new { estado = result.Status, detalhe = result.Message }),

            // 202: nada mudou, e não é erro — o processo ainda não foi
            // decidido. Promover por omissão seria a escalada que BR-20 fecha.
            ApplyApprovalOutcome.StillPending =>
                Results.Accepted(value: new { estado = result.Status, detalhe = result.Message }),

            ApplyApprovalOutcome.NotFound =>
                Results.NotFound(new { erro = result.Message }),

            _ => Results.Problem("Resultado inesperado ao aplicar a decisão."),
        };
    }

    private static async Task<IResult> ListContractsAsync(
        ListEmploymentContracts listContracts,
        Guid? employeeId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listContracts.ExecuteAsync(employeeId, cancellationToken));

    private static async Task<IResult> DrawContractAsync(
        DrawContractRequest request,
        DrawEmploymentContract drawContract,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await drawContract.ExecuteAsync(
            request.EmployeeId,
            request.Type,
            request.StartsOn,
            request.EndsOn,
            request.MonthlySalary,
            request.Currency ?? "AOA",
            request.Notes,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            DrawContractOutcome.Drawn =>
                Results.Created($"/hr/contracts?employeeId={request.EmployeeId}", new { contractId = result.ContractId }),

            DrawContractOutcome.EmployeeNotFound =>
                Results.NotFound(new { erro = result.Error }),

            // 409 e não 400: o pedido está bem formado, o que colide é o estado
            // actual — já existe um contrato em vigor no período.
            DrawContractOutcome.Overlaps =>
                Results.Conflict(new { erro = result.Error }),

            DrawContractOutcome.InvalidTerms =>
                Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["contrato"] = [result.Error!],
                }),

            _ => Results.Problem("Resultado inesperado ao celebrar o contrato."),
        };
    }

    private static async Task<IResult> TerminateContractAsync(
        Guid contractId,
        TerminateContractRequest request,
        TerminateEmploymentContract terminate,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var on = request.On ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await terminate.ExecuteAsync(contractId, on, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            TerminateContractOutcome.Terminated => Results.NoContent(),

            TerminateContractOutcome.NotFound =>
                Results.NotFound(new { erro = result.Error }),

            TerminateContractOutcome.Rejected =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao cessar o contrato."),
        };
    }

    private static async Task<IResult> ListAttendanceAsync(
        ListAttendance listAttendance,
        DateOnly? from,
        DateOnly? to,
        Guid? employeeId,
        bool? anomaliesOnly,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Sem intervalo, os últimos 30 dias. Uma consulta sem limites sobre
        // assiduidade cresce com o número de colaboradores vezes os dias, e o
        // pedido mais provável é "o que aconteceu recentemente".
        var inicio = from ?? hoje.AddDays(-30);
        var fim = to ?? hoje;

        if (fim < inicio)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["A data final não pode ser anterior à inicial."],
            });
        }

        return Results.Ok(await listAttendance.ExecuteAsync(
            inicio, fim, employeeId, anomaliesOnly ?? false, cancellationToken));
    }

    private static async Task<IResult> ClockAsync(
        ClockRequest request,
        ClockAttendance clockAttendance,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var day = request.Day ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await clockAttendance.ExecuteAsync(
            request.EmployeeId,
            day,
            request.Late ?? false,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            ClockOutcome.CheckedIn =>
                Results.Ok(new { recordId = result.RecordId, movimento = "entrada", at = result.At }),

            ClockOutcome.CheckedOut =>
                Results.Ok(new { recordId = result.RecordId, movimento = "saida", at = result.At }),

            ClockOutcome.EmployeeNotFound =>
                Results.NotFound(new { erro = result.Error }),

            ClockOutcome.Rejected =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao marcar o ponto."),
        };
    }

    private static async Task<IResult> RecordAbsenceAsync(
        RecordAbsenceRequest request,
        RecordAbsence recordAbsence,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await recordAbsence.ExecuteAsync(
            request.EmployeeId,
            request.Day,
            request.Justification,
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            AbsenceOutcome.Recorded =>
                Results.Created($"/hr/attendance?employeeId={request.EmployeeId}", new { recordId = result.RecordId }),

            AbsenceOutcome.Justified =>
                Results.Ok(new { recordId = result.RecordId }),

            AbsenceOutcome.EmployeeNotFound =>
                Results.NotFound(new { erro = result.Error }),

            AbsenceOutcome.Rejected =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao registar a ausência."),
        };
    }

    private static async Task<IResult> ListLeaveAsync(
        ListLeave listLeave,
        Guid? employeeId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listLeave.ExecuteAsync(employeeId, cancellationToken));

    private static async Task<IResult> RequestLeaveAsync(
        RequestLeaveRequest request,
        RequestLeave requestLeave,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await requestLeave.ExecuteAsync(
            request.EmployeeId, request.Type, request.StartsOn, request.EndsOn,
            request.Reason, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            // 202 e não 201: o pedido existe, mas **não é ausência ainda**. A
            // distinção é o ponto — só a decisão o torna efectivo.
            LeaveOutcome.Submitted =>
                Results.Accepted(
                    $"/hr/leave?employeeId={request.EmployeeId}",
                    new { leaveId = result.LeaveId, estado = "PendenteAprovacao", detalhe = result.Message }),

            LeaveOutcome.NotFound => Results.NotFound(new { erro = result.Message }),

            // 409: já há ausência pedida ou aprovada nesse período.
            LeaveOutcome.Overlaps => Results.Conflict(new { erro = result.Message }),

            // 501: não há motor de governança ligado neste ambiente.
            LeaveOutcome.ApprovalUnavailable =>
                Results.Problem(result.Message, statusCode: StatusCodes.Status501NotImplemented),

            // 409: a governança recusou receber — política em falta ou ambígua.
            LeaveOutcome.ApprovalRefusedSubmission =>
                Results.Conflict(new { erro = result.Message }),

            LeaveOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["ferias"] = [result.Message!] }),

            _ => Results.Problem("Resultado inesperado ao pedir férias."),
        };
    }

    private static async Task<IResult> CancelLeaveAsync(
        Guid leaveId,
        CancelLeave cancel,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await cancel.ExecuteAsync(leaveId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            LeaveOutcome.Cancelled => Results.NoContent(),
            LeaveOutcome.NotFound => Results.NotFound(new { erro = result.Message }),
            LeaveOutcome.Rejected => Results.Conflict(new { erro = result.Message }),
            _ => Results.Problem("Resultado inesperado ao retirar o pedido."),
        };
    }

    private static async Task<IResult> ApplyLeaveOutcomeAsync(
        Guid leaveId,
        ApplyLeaveApprovalOutcome apply,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await apply.ExecuteAsync(leaveId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            ApplyApprovalOutcome.Applied => Results.Ok(new { estado = result.Status }),

            ApplyApprovalOutcome.AlreadyResolved =>
                Results.Ok(new { estado = result.Status, detalhe = result.Message }),

            ApplyApprovalOutcome.StillPending =>
                Results.Accepted(value: new { estado = result.Status, detalhe = result.Message }),

            ApplyApprovalOutcome.NotFound => Results.NotFound(new { erro = result.Message }),

            _ => Results.Problem("Resultado inesperado ao aplicar a decisão."),
        };
    }

    private static async Task<IResult> ListBenefitsAsync(
        ListBenefits listBenefits,
        CancellationToken cancellationToken) =>
        Results.Ok(await listBenefits.ExecuteAsync(cancellationToken));

    private static async Task<IResult> CreateBenefitAsync(
        CreateBenefitRequest request,
        CreateBenefit createBenefit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await createBenefit.ExecuteAsync(
            request.Name, request.Kind, request.MonthlyValue,
            request.Currency ?? "AOA", request.Description,
            BuildAuditContext(http), cancellationToken);

        return result.Succeeded
            ? Results.Created("/hr/benefits", new { benefitId = result.BenefitId })
            : Results.ValidationProblem(new Dictionary<string, string[]> { ["beneficio"] = [result.Error!] });
    }

    private static async Task<IResult> ListEnrolmentsAsync(
        ListBenefitEnrolments listEnrolments,
        Guid? employeeId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listEnrolments.ExecuteAsync(employeeId, cancellationToken));

    private static async Task<IResult> EnrolAsync(
        EnrolRequest request,
        EnrolInBenefit enrol,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var startsOn = request.StartsOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await enrol.ExecuteAsync(
            request.EmployeeId, request.BenefitId, startsOn, BuildAuditContext(http), cancellationToken);

        return FromEnrol(result, "/hr/benefits/enrolments");
    }

    private static async Task<IResult> CancelEnrolmentAsync(
        Guid enrolmentId,
        CancelEnrolmentRequest request,
        CancelBenefitEnrolment cancel,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var on = request.On ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await cancel.ExecuteAsync(enrolmentId, on, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            EnrolOutcome.Done => Results.NoContent(),
            EnrolOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            EnrolOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao cancelar a adesão."),
        };
    }

    private static IResult FromEnrol(EnrolResult result, string location) =>
        result.Outcome switch
        {
            EnrolOutcome.Done => Results.Created(location, new { enrolmentId = result.EnrolmentId }),
            EnrolOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            EnrolOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado na adesão ao benefício."),
        };

    private static async Task<IResult> ListOpeningsAsync(
        ListJobOpenings listOpenings,
        CancellationToken cancellationToken) =>
        Results.Ok(await listOpenings.ExecuteAsync(cancellationToken));

    private static async Task<IResult> OpenOpeningAsync(
        OpenJobOpeningRequest request,
        OpenJobOpening openOpening,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await openOpening.ExecuteAsync(
            request.Title, request.DepartmentId, request.Vacancies ?? 1,
            request.Description, request.Requirements,
            BuildAuditContext(http), cancellationToken);

        return FromRecruitment(result, "/hr/recruitment/openings", "openingId");
    }

    private static async Task<IResult> CloseOpeningAsync(
        Guid openingId,
        CloseJobOpening closeOpening,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await closeOpening.ExecuteAsync(openingId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            RecruitmentOutcome.Done => Results.NoContent(),
            RecruitmentOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            RecruitmentOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao fechar a vaga."),
        };
    }

    private static async Task<IResult> ListCandidatesAsync(
        ListCandidates listCandidates,
        Guid? openingId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listCandidates.ExecuteAsync(openingId, cancellationToken));

    private static async Task<IResult> ApplyAsync(
        Guid openingId,
        ApplyRequest request,
        ApplyToJobOpening apply,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var appliedOn = request.AppliedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await apply.ExecuteAsync(
            openingId, request.FullName, request.Email, request.Phone, appliedOn,
            BuildAuditContext(http), cancellationToken);

        return FromRecruitment(result, $"/hr/recruitment/candidates?openingId={openingId}", "candidateId");
    }

    private static async Task<IResult> AdvanceCandidateAsync(
        Guid candidateId,
        AdvanceCandidateRequest request,
        AdvanceCandidate advance,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await advance.ExecuteAsync(
            candidateId, request.Stage, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            RecruitmentOutcome.Done => Results.NoContent(),
            RecruitmentOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 409: a fase pedida não é possível a partir da actual. O pedido
            // está bem formado — o que colide é o estado do funil.
            RecruitmentOutcome.Rejected => Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao avançar o candidato."),
        };
    }

    private static async Task<IResult> HireCandidateAsync(
        Guid candidateId,
        HireCandidateRequest request,
        HireCandidate hire,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await hire.ExecuteAsync(
            candidateId, request.DepartmentId, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            RecruitmentOutcome.Done =>
                Results.Created($"/hr/employees/{result.Id}", new { employeeId = result.Id }),

            RecruitmentOutcome.NotFound => Results.NotFound(new { erro = result.Error }),
            RecruitmentOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao contratar o candidato."),
        };
    }

    private static IResult FromRecruitment(RecruitmentResult result, string location, string idName) =>
        result.Outcome switch
        {
            RecruitmentOutcome.Done =>
                Results.Created(location, new Dictionary<string, object?> { [idName] = result.Id }),

            RecruitmentOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            RecruitmentOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["recrutamento"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado no recrutamento."),
        };

    private static async Task<IResult> ListLifecycleAsync(
        ListLifecycleProcesses listProcesses,
        string? kind,
        Guid? employeeId,
        CancellationToken cancellationToken) =>
        Results.Ok(await listProcesses.ExecuteAsync(kind, employeeId, cancellationToken));

    private static async Task<IResult> StartLifecycleAsync(
        StartLifecycleRequest request,
        StartLifecycleProcess start,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var tasks = (request.Tasks ?? [])
            .Select(t => new NewLifecycleTask(t.Title, t.Category, t.DueOn, t.Description))
            .ToList();

        var result = await start.ExecuteAsync(
            request.EmployeeId, request.Kind, request.LastWorkingDay, request.Reason,
            tasks, BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            LifecycleOutcome.Done =>
                Results.Created($"/hr/lifecycle?employeeId={request.EmployeeId}", new { processId = result.ProcessId }),

            LifecycleOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            LifecycleOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["processo"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado ao abrir o processo."),
        };
    }

    private static async Task<IResult> CompleteTaskAsync(
        Guid processId,
        Guid taskId,
        CompleteLifecycleTask completeTask,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await completeTask.ExecuteAsync(
            processId, taskId, BuildAuditContext(http), cancellationToken);

        return FromLifecycle(result);
    }

    private static async Task<IResult> CompleteLifecycleAsync(
        Guid processId,
        CompleteLifecycleProcess complete,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await complete.ExecuteAsync(processId, BuildAuditContext(http), cancellationToken);

        return FromLifecycle(result);
    }

    private static IResult FromLifecycle(LifecycleResult result) =>
        result.Outcome switch
        {
            LifecycleOutcome.Done => Results.NoContent(),
            LifecycleOutcome.NotFound => Results.NotFound(new { erro = result.Error }),

            // 409: faltam tarefas, ou o processo já está concluído. É o estado
            // que recusa, não o pedido.
            LifecycleOutcome.Rejected => Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado no processo."),
        };
}

// DTOs da fronteira HTTP. Entidades de domínio nunca são expostas.
public sealed record HireEmployeeRequest(string FullName, Guid? DepartmentId, Guid? UserId, DateTimeOffset? HiredOn);

/// <summary>
/// A conta a ligar. Só o <c>userId</c> — o colaborador vem da rota, e não há
/// mais nada a decidir: o vínculo é um par, não uma configuração.
/// </summary>
public sealed record LinkEmployeeAccountRequest(Guid UserId);

public sealed record CreateDepartmentRequest(string Name, Guid? ManagerId);

public sealed record CreatePositionRequest(string Name, int HierarchyLevel, bool GrantsApprovalAuthority);

public sealed record AssignPositionRequest(Guid PositionId, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <param name="Category">Classificação em RH: "contrato", "declaracao", "cv".</param>
public sealed record AttachDocumentRequest(Guid DocumentId, string Category);

/// <param name="Type">Permanent, FixedTerm ou Freelance.</param>
/// <param name="EndsOn">Obrigatório em FixedTerm e Freelance; proibido em Permanent.</param>
/// <param name="Currency">Código ISO 4217. Omitido, assume-se AOA.</param>
public sealed record DrawContractRequest(
    Guid EmployeeId,
    string Type,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    decimal MonthlySalary,
    string? Currency,
    string? Notes);

/// <param name="On">Data da cessação. Omitida, assume-se hoje.</param>
public sealed record TerminateContractRequest(DateOnly? On);

/// <param name="Day">Dia da marcação. Omitido, assume-se hoje.</param>
/// <param name="Late">
/// Se a entrada foi depois da hora prevista. Vem de quem marca porque o horário
/// do colaborador ainda não está modelado — ver Turnos e escalas.
/// </param>
public sealed record ClockRequest(Guid EmployeeId, DateOnly? Day, bool? Late);

/// <param name="Justification">
/// Omitida, regista uma falta por justificar. Preenchida sobre um dia já
/// marcado, justifica-o.
/// </param>
public sealed record RecordAbsenceRequest(Guid EmployeeId, DateOnly Day, string? Justification);

public sealed record CreateBenefitRequest(
    string Name,
    string Kind,
    decimal MonthlyValue,
    string? Currency,
    string? Description);

/// <param name="StartsOn">Início da adesão. Omitido, assume-se hoje.</param>
public sealed record EnrolRequest(Guid EmployeeId, Guid BenefitId, DateOnly? StartsOn);

public sealed record CancelEnrolmentRequest(DateOnly? On);

/// <param name="Vacancies">Omitido, assume-se um lugar.</param>
public sealed record OpenJobOpeningRequest(
    string Title,
    Guid? DepartmentId,
    int? Vacancies,
    string? Description,
    string? Requirements);

public sealed record ApplyRequest(string FullName, string? Email, string? Phone, DateOnly? AppliedOn);

/// <param name="Stage">
/// Fase seguinte: Screening, Interview, Offer ou Rejected. O funil avança um
/// passo de cada vez; para contratar use o endpoint próprio.
/// </param>
public sealed record AdvanceCandidateRequest(string Stage);

public sealed record HireCandidateRequest(Guid? DepartmentId);

/// <param name="Kind">Onboarding ou Offboarding.</param>
/// <param name="LastWorkingDay">Obrigatório em Offboarding.</param>
/// <param name="Tasks">
/// Tarefas iniciais da checklist. Um processo sem tarefas não pode ser
/// concluído — abri-lo vazio produz a lista que não verifica nada.
/// </param>
public sealed record StartLifecycleRequest(
    Guid EmployeeId,
    string Kind,
    DateOnly? LastWorkingDay,
    string? Reason,
    IReadOnlyList<LifecycleTaskRequest>? Tasks);

public sealed record LifecycleTaskRequest(string Title, string Category, DateOnly? DueOn, string? Description);

/// <param name="Type">Annual, Sick, Parental ou Unpaid.</param>
/// <param name="EndsOn">Último dia de ausência, inclusive.</param>
public sealed record RequestLeaveRequest(
    Guid EmployeeId,
    string Type,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string? Reason);
