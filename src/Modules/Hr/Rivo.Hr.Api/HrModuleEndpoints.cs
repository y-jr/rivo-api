using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rivo.Audit.Contracts;
using Rivo.Hr.Application.UseCases;
using Rivo.Hr.Contracts;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Hr.Api;

public static class HrModuleEndpoints
{
    public static IEndpointRouteBuilder MapHrModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/hr");

        group.MapGet("/employees", ListEmployeesAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead)
            .Produces<IReadOnlyList<EmployeeView>>()
            .ProducesValidationProblem();

        group.MapPost("/employees", HireEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Correcções (ADR-063) ---
        //
        // **PUT para corrigir, POST para actos.** O resto do módulo usa POST
        // porque quase tudo aqui é um acontecimento com nome próprio — admitir,
        // ligar uma conta, atribuir um cargo. Corrigir não é acontecimento: é
        // repor o valor certo num campo que estava errado, e o verbo que diz
        // isso é PUT. A transferência de departamento, essa, continua a ser um
        // acto, e por isso tem sub-recurso e POST.
        group.MapPut("/employees/{employeeId:guid}", CorrectEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/employees/{employeeId:guid}/department", TransferEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/employees/{employeeId:guid}", GetEmployeeAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead)
            .Produces<EmployeeDetailView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Liga uma conta de identity a um colaborador já admitido (ADR-051).
        //
        // Permissão própria, e não EmployeesWrite: ao contrário do `commercial`
        // — que usa a permissão de escrita do Cliente para o equivalente
        // (ADR-043) — aqui o vínculo concede o que o Cargo confere. Desde o
        // ADR-050 é ele que determina quem decide aprovações, e portanto quem
        // o cria escolhe, indirectamente, quem aprova.
        group.MapPost("/employees/{employeeId:guid}/account", LinkEmployeeAccountAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Desligar (ADR-052). Mesma permissão de ligar: desligar é uma perda
        // de capacidade, não um ganho, e exigir mais do que para ligar
        // atrasaria a correcção de um vínculo errado — que é resposta a
        // incidente, não operação de rotina.
        group.MapDelete("/employees/{employeeId:guid}/account", UnlinkEmployeeAccountAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // O histórico do vínculo (ADR-053). Mesma permissão de o gerir, e não
        // `hr.employees.read`: expõe o mapa conta↔pessoa ao longo do tempo, que
        // é informação de segurança e não de organograma. Quem só precisa de
        // saber quem trabalha na empresa não precisa de saber com que conta.
        group.MapGet("/employees/{employeeId:guid}/account-history", GetAccountHistoryAsync)
            .RequireAuthorization(HrPermissions.EmployeesLinkAccount)
            .Produces<IReadOnlyList<EmployeeAccountLinkView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/departments", ListDepartmentsAsync)
            .RequireAuthorization(HrPermissions.DepartmentsRead)
            .Produces<IReadOnlyList<DepartmentView>>()
            .ProducesValidationProblem();

        group.MapPost("/departments", CreateDepartmentAsync)
            .RequireAuthorization(HrPermissions.DepartmentsWrite)
            .Produces(StatusCodes.Status201Created);

        group.MapPut("/departments/{departmentId:guid}", CorrectDepartmentAsync)
            .RequireAuthorization(HrPermissions.DepartmentsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/positions", ListPositionsAsync)
            .RequireAuthorization(HrPermissions.PositionsRead)
            .Produces<IReadOnlyList<PositionView>>()
            .ProducesValidationProblem();

        // Catálogo de Cargos: só Admin. Quem controla a marca de autoridade
        // controla, indirectamente, quem pode vir a aprovar (ADR-015).
        group.MapPost("/positions", CreatePositionAsync)
            .RequireAuthorization(HrPermissions.PositionsWrite)
            .Produces(StatusCodes.Status201Created);

        // Corrigir o catálogo pede a mesma permissão que o criar, e por isso
        // fica fora do perfil HR (ADR-015).
        group.MapPut("/positions/{positionId:guid}", CorrectPositionAsync)
            .RequireAuthorization(HrPermissions.PositionsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Atribuição: operação corrente de RH.
        group.MapPost("/employees/{employeeId:guid}/positions", AssignPositionAsync)
            .RequireAuthorization(HrPermissions.PositionsAssign)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        // Atribuição directa, ignorando BR-20 mesmo quando o Cargo confere
        // autoridade de aprovação (ADR-058). Rota e permissão à parte da
        // atribuição normal, de propósito: existe só para a conta de operação
        // `SuperAdmin` resolver o arranque circular do motor de aprovação, e
        // nenhum outro perfil — nem `Admin` — a tem.
        group.MapPost("/employees/{employeeId:guid}/positions/direct", AssignPositionDirectAsync)
            .RequireAuthorization(HrPermissions.PositionsAssignDirect)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Aplica a decisão já tomada em governança a uma atribuição pendente.
        //
        // É `hr` que pergunta: `approval` não pode modificar dados de negócio
        // do módulo de origem, por isso a promoção a efectiva parte daqui.
        group.MapPost("/position-assignments/{assignmentId:guid}/approval-outcome", ApplyApprovalOutcomeAsync)
            .RequireAuthorization(HrPermissions.PositionsAssign)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Encerra a ocupação de um Cargo (#39): sempre explícito, nunca
        // automático — a mesma permissão que já protege AssignPositionAsync,
        // porque é a mesma operação de negócio vista do outro lado.
        group.MapPost("/position-assignments/{assignmentId:guid}/closure", EndPositionAssignmentAsync)
            .RequireAuthorization(HrPermissions.PositionsAssign)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Anexar exige permissão de escrita em colaboradores, não de
        // documentos: está a alterar-se o registo do colaborador. O upload do
        // ficheiro é que exige `documents.write`.
        group.MapPost("/employees/{employeeId:guid}/documents", AttachDocumentAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/employees/{employeeId:guid}/documents", ListEmployeeDocumentsAsync)
            .RequireAuthorization(HrPermissions.EmployeesRead)
            .Produces<IReadOnlyList<EmployeeDocumentView>>();

        // Contratos de trabalho. Permissão própria e não `employees.read`: a
        // lista traz a remuneração acordada, que é a informação mais sensível
        // do módulo.
        group.MapGet("/contracts", ListContractsAsync)
            .RequireAuthorization(HrPermissions.ContractsRead)
            .Produces<IReadOnlyList<EmploymentContractView>>()
            .ProducesValidationProblem();

        group.MapPost("/contracts", DrawContractAsync)
            .RequireAuthorization(HrPermissions.ContractsWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/contracts/{contractId:guid}/termination", TerminateContractAsync)
            .RequireAuthorization(HrPermissions.ContractsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Assiduidade.
        group.MapGet("/attendance", ListAttendanceAsync)
            .RequireAuthorization(HrPermissions.AttendanceRead)
            .Produces<IReadOnlyList<AttendanceView>>()
            .ProducesValidationProblem();

        // Marcação de ponto: uma rota, porque é um botão só. Entrada ou saída,
        // decide o servidor consoante o dia já esteja aberto.
        group.MapPost("/attendance/clock", ClockAsync)
            .RequireAuthorization(HrPermissions.AttendanceWrite)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/attendance/absences", RecordAbsenceAsync)
            .RequireAuthorization(HrPermissions.AttendanceWrite)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Férias e outras ausências planeadas. Passam por governança: um pedido
        // pendente **não é ausência** (mesmo princípio de BR-20).
        group.MapGet("/leave", ListLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveRead)
            .Produces<IReadOnlyList<LeaveView>>()
            .ProducesValidationProblem();

        group.MapPost("/leave", RequestLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        group.MapPost("/leave/{leaveId:guid}/cancellation", CancelLeaveAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/leave/{leaveId:guid}/approval-outcome", ApplyLeaveOutcomeAsync)
            .RequireAuthorization(HrPermissions.LeaveWrite)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Benefícios: catálogo e adesões.
        group.MapGet("/benefits", ListBenefitsAsync)
            .RequireAuthorization(HrPermissions.BenefitsRead)
            .Produces<IReadOnlyList<BenefitView>>()
            .ProducesValidationProblem();

        group.MapPost("/benefits", CreateBenefitAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/benefits/enrolments", ListEnrolmentsAsync)
            .RequireAuthorization(HrPermissions.BenefitsRead)
            .Produces<IReadOnlyList<BenefitEnrolmentView>>()
            .ProducesValidationProblem();

        group.MapPost("/benefits/enrolments", EnrolAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/benefits/enrolments/{enrolmentId:guid}/cancellation", CancelEnrolmentAsync)
            .RequireAuthorization(HrPermissions.BenefitsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Recrutamento: vagas e funil de candidatos.
        group.MapGet("/recruitment/openings", ListOpeningsAsync)
            .RequireAuthorization(HrPermissions.RecruitmentRead)
            .Produces<IReadOnlyList<JobOpeningView>>()
            .ProducesValidationProblem();

        group.MapPost("/recruitment/openings", OpenOpeningAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/recruitment/openings/{openingId:guid}/closure", CloseOpeningAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/recruitment/candidates", ListCandidatesAsync)
            .RequireAuthorization(HrPermissions.RecruitmentRead)
            .Produces<IReadOnlyList<CandidateView>>()
            .ProducesValidationProblem();

        group.MapPost("/recruitment/openings/{openingId:guid}/candidates", ApplyAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/recruitment/candidates/{candidateId:guid}/stage", AdvanceCandidateAsync)
            .RequireAuthorization(HrPermissions.RecruitmentWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Contratar cria um Colaborador — por isso exige também escrita em
        // colaboradores, e não só em recrutamento.
        group.MapPost("/recruitment/candidates/{candidateId:guid}/hire", HireCandidateAsync)
            .RequireAuthorization(HrPermissions.EmployeesWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Entrada e saída, conduzidas por checklist.
        group.MapGet("/lifecycle", ListLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleRead)
            .Produces<IReadOnlyList<LifecycleProcessView>>()
            .ProducesValidationProblem();

        group.MapPost("/lifecycle", StartLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/lifecycle/{processId:guid}/tasks/{taskId:guid}/completion", CompleteTaskAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/lifecycle/{processId:guid}/completion", CompleteLifecycleAsync)
            .RequireAuthorization(HrPermissions.LifecycleWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
            _ => Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
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

    /// <summary>
    /// Interpreta `page`/`pageSize` uma só vez, para as onze listagens que
    /// ganharam paginação real (ADR-068) não repetirem a mesma validação.
    /// </summary>
    private static (PageRequest? Pagina, IResult? Erro) ResolverPagina(int? page, int? pageSize)
    {
        if (!Pagination.TryParse(page, pageSize, out var pagina, out var mensagem))
        {
            return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["pagina"] = [mensagem!] }));
        }

        return (pagina, null);
    }

    /// <summary>Só escreve cabeçalhos quando há página pedida — sem isso, nada muda na resposta.</summary>
    private static void EscreverCabecalhosDePagina(HttpResponse response, PageRequest? pagina, int? total)
    {
        if (pagina is { } p)
        {
            response.Headers["X-Page"] = p.Page.ToString();
            response.Headers["X-Page-Size"] = p.PageSize.ToString();
            response.Headers["X-Total-Count"] = total!.Value.ToString();
        }
    }

    private static async Task<IResult> ListEmployeesAsync(
        ListEmployees listEmployees,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listEmployees.ExecuteAsync(pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

    private static async Task<IResult> GetEmployeeAsync(
        Guid employeeId,
        IEmployeeDirectory directory,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Passa pelo contrato publicado, e não pela persistência: é o mesmo
        // caminho que outros módulos usarão (ADR-010).
        var reference = await directory.FindAsync(employeeId, clock.GetUtcNow(), cancellationToken);

        if (reference is null)
        {
            return Results.NotFound();
        }

        // Mesmo nome de campo e mesma serialização de `status` que
        // GET /hr/employees (EmployeeView) — a lista e o detalhe descrevem o
        // mesmo colaborador e não podem divergir na forma. `DisplayName`
        // mantém-se como está no contrato interno (EmployeeReference é usado
        // por outros módulos via ADR-010); só a resposta HTTP é uniformizada.
        return Results.Ok(new EmployeeDetailView(
            reference.EmployeeId,
            reference.DisplayName,
            reference.Status.ToString(),
            reference.DepartmentId,
            reference.CurrentPosition,
            reference.UserId));
    }

    private sealed record EmployeeDetailView(
        Guid EmployeeId,
        string FullName,
        string Status,
        Guid? DepartmentId,
        PositionReference? CurrentPosition,
        Guid? UserId);

    private static async Task<IResult> HireEmployeeAsync(
        HireEmployeeRequest request,
        HireEmployee hireEmployee,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // A admissão deixou de criar vínculos (ADR-054). Recusa-se em vez de
        // ignorar: remover o campo do DTO faria o System.Text.Json descartá-lo
        // em silêncio, e quem continuasse a enviá-lo ficaria convencido de que
        // tinha ligado a conta. Uma alteração de contrato tem de ser ruidosa
        // para quem ainda depende do contrato antigo.
        if (request.UserId is not null)
        {
            return Results.BadRequest(new
            {
                erro = "A admissão já não associa contas. Admita o colaborador e depois use "
                     + "POST /hr/employees/{employeeId}/account, que exige hr.employees.link_account.",
            });
        }

        var result = await hireEmployee.ExecuteAsync(
            request.FullName,
            request.DepartmentId,
            request.HiredOn ?? clock.GetUtcNow(),
            BuildAuditContext(http),
            cancellationToken);

        return result.Outcome switch
        {
            HireEmployeeOutcome.Hired =>
                Results.Created($"/hr/employees/{result.EmployeeId}", new { employeeId = result.EmployeeId }),
            HireEmployeeOutcome.DepartmentNotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
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

            LinkEmployeeAccountOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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

            UnlinkEmployeeAccountOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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
            ? Results.Problem("Colaborador não encontrado.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(historico);
    }

    /// <summary>
    /// Traduz o desfecho de uma correcção. Escrito uma vez porque as quatro são
    /// iguais nisto: 204 quando fica feito, 404 quando o registo não existe, e
    /// 400 quando o pedido refere algo que não existe ou traz um campo vazio.
    /// </summary>
    private static IResult Responder(CorrectionResult resultado) =>
        resultado.Outcome switch
        {
            // 204 e não o objecto corrigido: quem chama já sabe o que enviou, e
            // devolver o registo convidaria o ecrã a confiar nele em vez de
            // reler a lista -- que é onde as outras correcções aparecem.
            CorrectionOutcome.Corrected => Results.NoContent(),

            CorrectionOutcome.NotFound => Results.Problem("Registo não encontrado.", statusCode: StatusCodes.Status404NotFound),

            CorrectionOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["correccao"] = [resultado.Error!] }),

            _ => Results.Problem("Resultado inesperado na correcção."),
        };

    private static async Task<IResult> CorrectEmployeeAsync(
        Guid employeeId,
        CorrectEmployeeRequest request,
        CorrectEmployee correct,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Responder(await correct.ExecuteAsync(
            employeeId, request.FullName, BuildAuditContext(http), cancellationToken));

    private static async Task<IResult> TransferEmployeeAsync(
        Guid employeeId,
        TransferEmployeeRequest request,
        TransferEmployee transfer,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Responder(await transfer.ExecuteAsync(
            employeeId, request.DepartmentId, BuildAuditContext(http), cancellationToken));

    private static async Task<IResult> CorrectDepartmentAsync(
        Guid departmentId,
        CorrectDepartmentRequest request,
        CorrectDepartment correct,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Responder(await correct.ExecuteAsync(
            departmentId, request.Name, request.ManagerId, BuildAuditContext(http), cancellationToken));

    private static async Task<IResult> CorrectPositionAsync(
        Guid positionId,
        CorrectPositionRequest request,
        CorrectPosition correct,
        HttpContext http,
        CancellationToken cancellationToken) =>
        Responder(await correct.ExecuteAsync(
            positionId, request.Name, request.HierarchyLevel, BuildAuditContext(http), cancellationToken));

    private static async Task<IResult> ListDepartmentsAsync(
        ListDepartments listDepartments,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listDepartments.ExecuteAsync(pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listPositions.ExecuteAsync(pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
                Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),

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

    private static async Task<IResult> AssignPositionDirectAsync(
        Guid employeeId,
        AssignPositionRequest request,
        AssignPosition assignPosition,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await assignPosition.ExecuteDirectAsync(
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
                Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),

            AssignPositionOutcome.PositionOccupied =>
                Results.Conflict(new { erro = result.Message }),

            _ => Results.Problem("Resultado inesperado ao atribuir o cargo directamente."),
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
                Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),

            // 409: aprovada, mas o cargo já tem quem o ocupe (#39). Nunca se
            // promove por cima de quem já lá está — encerra-se a actual
            // primeiro, e chama-se isto outra vez (idempotente).
            ApplyApprovalOutcome.Blocked =>
                Results.Conflict(new { estado = result.Status, erro = result.Message }),

            _ => Results.Problem("Resultado inesperado ao aplicar a decisão."),
        };
    }

    private static async Task<IResult> EndPositionAssignmentAsync(
        Guid assignmentId,
        EndPositionAssignmentRequest request,
        EndPositionAssignment endAssignment,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await endAssignment.ExecuteAsync(
            assignmentId, request.EndedOn ?? clock.GetUtcNow(), BuildAuditContext(http), cancellationToken);

        return result.Outcome switch
        {
            PositionAssignmentClosureOutcome.Ended => Results.NoContent(),

            PositionAssignmentClosureOutcome.NotFound =>
                Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

            // 409: não é efectiva, já tinha terminado, ou a data de fim é
            // anterior ao início — conflito com o estado actual, não pedido
            // malformado.
            PositionAssignmentClosureOutcome.Rejected =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao encerrar a atribuição."),
        };
    }

    private static async Task<IResult> ListContractsAsync(
        ListEmploymentContracts listContracts,
        Guid? employeeId,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listContracts.ExecuteAsync(employeeId, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
                Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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
                Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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
        int? page,
        int? pageSize,
        TimeProvider clock,
        HttpResponse response,
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

        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listAttendance.ExecuteAsync(
            inicio, fim, employeeId, anomaliesOnly ?? false, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
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
                Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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
                Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

            AbsenceOutcome.Rejected =>
                Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado ao registar a ausência."),
        };
    }

    private static async Task<IResult> ListLeaveAsync(
        ListLeave listLeave,
        Guid? employeeId,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listLeave.ExecuteAsync(employeeId, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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

            LeaveOutcome.NotFound => Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),

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
            LeaveOutcome.NotFound => Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
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

            ApplyApprovalOutcome.NotFound => Results.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),

            _ => Results.Problem("Resultado inesperado ao aplicar a decisão."),
        };
    }

    private static async Task<IResult> ListBenefitsAsync(
        ListBenefits listBenefits,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listBenefits.ExecuteAsync(pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listEnrolments.ExecuteAsync(employeeId, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
            EnrolOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            EnrolOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao cancelar a adesão."),
        };
    }

    private static IResult FromEnrol(EnrolResult result, string location) =>
        result.Outcome switch
        {
            EnrolOutcome.Done => Results.Created(location, new { enrolmentId = result.EnrolmentId }),
            EnrolOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            EnrolOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado na adesão ao benefício."),
        };

    private static async Task<IResult> ListOpeningsAsync(
        ListJobOpenings listOpenings,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listOpenings.ExecuteAsync(pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
            RecruitmentOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            RecruitmentOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao fechar a vaga."),
        };
    }

    private static async Task<IResult> ListCandidatesAsync(
        ListCandidates listCandidates,
        Guid? openingId,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listCandidates.ExecuteAsync(openingId, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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
            RecruitmentOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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

            RecruitmentOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            RecruitmentOutcome.Rejected => Results.Conflict(new { erro = result.Error }),
            _ => Results.Problem("Resultado inesperado ao contratar o candidato."),
        };
    }

    private static IResult FromRecruitment(RecruitmentResult result, string location, string idName) =>
        result.Outcome switch
        {
            RecruitmentOutcome.Done =>
                Results.Created(location, new Dictionary<string, object?> { [idName] = result.Id }),

            RecruitmentOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

            RecruitmentOutcome.Rejected =>
                Results.ValidationProblem(new Dictionary<string, string[]> { ["recrutamento"] = [result.Error!] }),

            _ => Results.Problem("Resultado inesperado no recrutamento."),
        };

    private static async Task<IResult> ListLifecycleAsync(
        ListLifecycleProcesses listProcesses,
        string? kind,
        Guid? employeeId,
        int? page,
        int? pageSize,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var (pagina, erro) = ResolverPagina(page, pageSize);
        if (erro is not null)
        {
            return erro;
        }

        var (itens, total) = await listProcesses.ExecuteAsync(kind, employeeId, pagina, cancellationToken);
        EscreverCabecalhosDePagina(response, pagina, total);
        return Results.Ok(itens);
    }

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

            LifecycleOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

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
            LifecycleOutcome.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),

            // 409: faltam tarefas, ou o processo já está concluído. É o estado
            // que recusa, não o pedido.
            LifecycleOutcome.Rejected => Results.Conflict(new { erro = result.Error }),

            _ => Results.Problem("Resultado inesperado no processo."),
        };
}

// DTOs da fronteira HTTP. Entidades de domínio nunca são expostas.
/// <param name="UserId">
/// ⚠ <strong>Já não é aceite (ADR-054).</strong> Continua declarado só para
/// poder ser <em>recusado</em> com 400: apagá-lo do contrato faria o
/// desserializador ignorá-lo em silêncio, e quem ainda o enviasse ficaria a
/// pensar que tinha ligado a conta.
///
/// <para>
/// Pode desaparecer quando não houver cliente a enviá-lo.
/// </para>
/// </param>
public sealed record HireEmployeeRequest(string FullName, Guid? DepartmentId, Guid? UserId, DateTimeOffset? HiredOn);

/// <summary>
/// A conta a ligar. Só o <c>userId</c> — o colaborador vem da rota, e não há
/// mais nada a decidir: o vínculo é um par, não uma configuração.
/// </summary>
public sealed record LinkEmployeeAccountRequest(Guid UserId);

public sealed record CreateDepartmentRequest(string Name, Guid? ManagerId);

/// <param name="FullName">O nome corrigido. Vazio é recusado, não apagado.</param>
public sealed record CorrectEmployeeRequest(string FullName);

/// <param name="DepartmentId">
/// Nulo tira o colaborador de qualquer departamento — é escolha válida, e não
/// campo esquecido.
/// </param>
public sealed record TransferEmployeeRequest(Guid? DepartmentId);

public sealed record CorrectDepartmentRequest(string Name, Guid? ManagerId);

/// <summary>
/// Sem <c>GrantsApprovalAuthority</c>, de propósito: a marca de autoridade não
/// se corrige. Ver <c>Position.Correct</c> para a razão.
/// </summary>
public sealed record CorrectPositionRequest(string Name, int HierarchyLevel);

public sealed record CreatePositionRequest(string Name, int HierarchyLevel, bool GrantsApprovalAuthority);

public sealed record AssignPositionRequest(Guid PositionId, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <param name="EndedOn">Omisso: agora (#39).</param>
public sealed record EndPositionAssignmentRequest(DateTimeOffset? EndedOn);

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
