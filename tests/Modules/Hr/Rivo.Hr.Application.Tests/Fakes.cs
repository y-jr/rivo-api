using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Hr.Application.Tests;

/// <summary>
/// Guarda colaboradores em memória e conta as gravações.
///
/// A contagem existe para os testes poderem distinguir "recusou" de "aceitou e
/// não mudou nada" — sem ela, um caso de uso que devolvesse sucesso sem gravar
/// passaria despercebido.
/// </summary>
internal sealed class FakeHrStore : HrStoreParcial
{
    private readonly List<Employee> _colaboradores = [];
    private readonly List<EmployeeAccountLink> _episodios = [];
    private readonly List<Department> _departamentos = [];
    private readonly List<Position> _cargos = [];
    private readonly List<PositionAssignment> _atribuicoes = [];
    private readonly List<LeaveRequest> _pedidosDeFerias = [];
    private readonly List<EmploymentContract> _contratos = [];
    private readonly List<AttendanceRecord> _marcacoes = [];

    public int Gravacoes { get; private set; }

    /// <summary>
    /// Mesma lógica de paginação real do `HrStore` (ADR-068), aplicada em
    /// memória: sem página, devolve tudo; com página, corta a colecção já
    /// ordenada e devolve o total de antes do corte.
    /// </summary>
    private static (IReadOnlyList<T> Items, int? TotalCount) Paginar<T>(IEnumerable<T> ordenados, PageRequest? pagina)
    {
        var lista = ordenados.ToList();

        if (pagina is not { } p)
        {
            return (lista, null);
        }

        return ([.. lista.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize)], lista.Count);
    }

    public LeaveRequest AdicionarFerias(LeaveRequest pedido)
    {
        _pedidosDeFerias.Add(pedido);
        return pedido;
    }

    public EmploymentContract AdicionarContrato(EmploymentContract contrato)
    {
        _contratos.Add(contrato);
        return contrato;
    }

    public AttendanceRecord AdicionarMarcacao(AttendanceRecord registo)
    {
        _marcacoes.Add(registo);
        return registo;
    }

    public override Task<(IReadOnlyList<Department> Items, int? TotalCount)> ListDepartmentsAsync(
        PageRequest? pagina, CancellationToken cancellationToken) =>
        Task.FromResult(Paginar(_departamentos.OrderBy(d => d.Name), pagina));

    public override Task<(IReadOnlyList<Position> Items, int? TotalCount)> ListPositionsAsync(
        PageRequest? pagina, CancellationToken cancellationToken) =>
        Task.FromResult(Paginar(_cargos.OrderBy(p => p.HierarchyLevel).ThenBy(p => p.Name), pagina));

    public override Task<(IReadOnlyList<LeaveRequest> Items, int? TotalCount)> ListLeaveAsync(
        Guid? employeeId, PageRequest? pagina, CancellationToken cancellationToken)
    {
        var filtrados = employeeId is { } id
            ? _pedidosDeFerias.Where(l => l.EmployeeId == id)
            : _pedidosDeFerias.AsEnumerable();

        return Task.FromResult(Paginar(filtrados.OrderByDescending(l => l.StartsOn), pagina));
    }

    public override Task<(IReadOnlyList<EmploymentContract> Items, int? TotalCount)> ListContractsAsync(
        PageRequest? pagina, CancellationToken cancellationToken) =>
        Task.FromResult(Paginar(_contratos.OrderByDescending(c => c.StartsOn), pagina));

    public override Task<IReadOnlyList<EmploymentContract>> ListContractsForEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmploymentContract>>(
            [.. _contratos.Where(c => c.EmployeeId == employeeId).OrderByDescending(c => c.StartsOn)]);

    public override Task<(IReadOnlyList<AttendanceRecord> Items, int? TotalCount)> ListAttendanceAsync(
        DateOnly from, DateOnly to, Guid? employeeId, bool anomaliesOnly, PageRequest? pagina,
        CancellationToken cancellationToken)
    {
        var filtrados = _marcacoes.Where(a => a.Day >= from && a.Day <= to);

        if (employeeId is { } id)
        {
            filtrados = filtrados.Where(a => a.EmployeeId == id);
        }

        if (anomaliesOnly)
        {
            filtrados = filtrados.Where(a => a.IsAnomaly);
        }

        return Task.FromResult(Paginar(filtrados.OrderByDescending(a => a.Day), pagina));
    }

    /// <summary>Os episódios de histórico gravados, por ordem de criação.</summary>
    public IReadOnlyList<EmployeeAccountLink> Episodios => _episodios;

    public Employee Admitir(string nome, Guid? userId = null)
    {
        var colaborador = Employee.Hire(nome, departmentId: null, userId, DateTimeOffset.UnixEpoch);
        _colaboradores.Add(colaborador);

        // Um colaborador admitido já com conta tem episódio aberto, tal como
        // a migração de retroactivo garante em base (ADR-053). Sem isto, os
        // testes de desligamento exercitariam o caminho excepcional em vez do
        // normal.
        if (userId is { } conta)
        {
            _episodios.Add(EmployeeAccountLink.Open(
                colaborador.Id, conta, DateTimeOffset.UnixEpoch, linkedByUserId: null));
        }

        return colaborador;
    }

    public override Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken)
    {
        _colaboradores.Add(employee);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verdadeiro para qualquer identificador <strong>enquanto nenhum
    /// departamento for registado</strong> — que é como os testes de admissão
    /// sempre o usaram. A partir do momento em que um teste chama
    /// <see cref="CriarDepartamento"/>, passa a responder pela lista, e um
    /// identificador desconhecido deixa de existir.
    /// </summary>
    public override Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(_departamentos.Count == 0 || _departamentos.Any(d => d.Id == departmentId));

    public Department CriarDepartamento(string nome, Guid? managerId = null)
    {
        var departamento = Department.Create(nome, managerId);
        _departamentos.Add(departamento);

        return departamento;
    }

    public Position CriarCargo(string nome, int nivel = 5, bool confereAutoridade = false)
    {
        var cargo = Position.Create(nome, nivel, confereAutoridade);
        _cargos.Add(cargo);

        return cargo;
    }

    public override Task<Department?> FindDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(_departamentos.SingleOrDefault(d => d.Id == departmentId));

    public override Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken) =>
        Task.FromResult(_cargos.SingleOrDefault(p => p.Id == positionId));

    /// <summary>Semeia uma atribuição já efectiva, sem passar por <c>AssignPosition</c> — para preparar o cenário de um teste.</summary>
    public PositionAssignment AtribuirCargo(
        Guid employeeId, Guid positionId, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo = null)
    {
        var atribuicao = PositionAssignment.CreateEffective(employeeId, positionId, effectiveFrom, effectiveTo);
        _atribuicoes.Add(atribuicao);
        return atribuicao;
    }

    /// <summary>Semeia uma atribuição Pending já ligada a um processo de aprovação — para testar <c>ApplyPositionApprovalOutcome</c>.</summary>
    public PositionAssignment AtribuirCargoPendente(
        Guid employeeId, Guid positionId, DateTimeOffset effectiveFrom, Guid requestId)
    {
        var atribuicao = PositionAssignment.CreatePending(employeeId, positionId, effectiveFrom, null);
        atribuicao.LinkToApprovalRequest(requestId);
        _atribuicoes.Add(atribuicao);
        return atribuicao;
    }

    public override Task AddAssignmentAsync(PositionAssignment assignment, CancellationToken cancellationToken)
    {
        _atribuicoes.Add(assignment);
        return Task.CompletedTask;
    }

    public override Task<PositionAssignment?> FindAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        Task.FromResult(_atribuicoes.SingleOrDefault(a => a.Id == assignmentId));

    public override Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForPositionAsync(
        Guid positionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PositionAssignment>>(
            [.. _atribuicoes.Where(a => a.PositionId == positionId)]);

    public override Task AddAccountLinkAsync(EmployeeAccountLink link, CancellationToken cancellationToken)
    {
        _episodios.Add(link);
        return Task.CompletedTask;
    }

    public override Task<EmployeeAccountLink?> FindOpenAccountLinkAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_episodios.SingleOrDefault(l => l.EmployeeId == employeeId && l.IsOpen));

    public override Task<IReadOnlyList<EmployeeAccountLink>> ListAccountLinksAsync(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmployeeAccountLink>>(
            [.. _episodios.Where(l => l.EmployeeId == employeeId).OrderByDescending(l => l.LinkedOn)]);

    public override Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(_colaboradores.SingleOrDefault(e => e.Id == employeeId));

    public override Task<Employee?> FindEmployeeByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_colaboradores.SingleOrDefault(e => e.UserId == userId));

    public override Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Recolhe o que foi auditado. Escrita à mão, sem biblioteca de mocks
/// (ADR-022).
/// </summary>
internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<AuditRecord> Registos { get; } = [];

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Registos.Add(record);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sem motor de governança ligado — o que os testes de Cargos sem autoridade
/// de aprovação precisam, e nada mais. Chamar qualquer membro é um teste a
/// exercitar um caminho que não previu (mesma disciplina de <c>HrStoreParcial</c>).
/// </summary>
internal sealed class FakeHrApprovalSubmission : IHrApprovalSubmission
{
    public bool IsAvailable => false;

    public Task<HrApprovalSubmissionResult> SubmitAsync(
        HrApprovalProcess process, Guid sourceReference, Guid requestedByEmployeeId,
        Guid? departmentId, string summary, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu submeter a aprovação.");

    public Task<HrApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu consultar o estado de aprovação.");
}

/// <summary>Um estado de aprovação fixo — para testar <c>ApplyPositionApprovalOutcome</c> sem um motor real.</summary>
internal sealed class FakeApprovalOutcome(HrApprovalState estado) : IHrApprovalSubmission
{
    public bool IsAvailable => true;

    public Task<HrApprovalSubmissionResult> SubmitAsync(
        HrApprovalProcess process, Guid sourceReference, Guid requestedByEmployeeId,
        Guid? departmentId, string summary, CancellationToken cancellationToken) =>
        throw new NotSupportedException("O teste não previu submeter a aprovação.");

    public Task<HrApprovalState> GetStateAsync(Guid approvalRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(estado);
}

/// <summary>
/// Relógio parado. Escrito à mão em vez de `FakeTimeProvider` (ADR-022), e
/// igual ao de `approval` — são quatro linhas, e partilhá-las obrigaria a um
/// projecto de utilitários de teste que não se justifica por isto.
/// </summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
