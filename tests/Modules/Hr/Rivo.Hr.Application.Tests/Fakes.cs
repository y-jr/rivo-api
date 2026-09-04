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

    public int Gravacoes { get; private set; }

    public Employee Admitir(string nome, Guid? userId = null)
    {
        var colaborador = Employee.Hire(nome, departmentId: null, userId, DateTimeOffset.UnixEpoch);
        _colaboradores.Add(colaborador);
        return colaborador;
    }

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
