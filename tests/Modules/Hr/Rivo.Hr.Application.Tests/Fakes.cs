using Rivo.Audit.Contracts;
using Rivo.Hr.Domain;

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

    public int Gravacoes { get; private set; }

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

    public override Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(true);

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
/// Relógio parado. Escrito à mão em vez de `FakeTimeProvider` (ADR-022), e
/// igual ao de `approval` — são quatro linhas, e partilhá-las obrigaria a um
/// projecto de utilitários de teste que não se justifica por isto.
/// </summary>
internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
