using Rivo.Audit.Contracts;
using Rivo.Hr.Application.Abstractions;
using Rivo.Hr.Domain;
using Rivo.SharedKernel.Contracts;

namespace Rivo.Hr.Application.UseCases;

// --- Departamentos ---

public sealed class ListDepartments(IHrStore store)
{
    public async Task<(IReadOnlyList<DepartmentView> Items, int? TotalCount)> ExecuteAsync(
        PageRequest? pagina, CancellationToken cancellationToken)
    {
        var (departments, total) = await store.ListDepartmentsAsync(pagina, cancellationToken);

        return ([.. departments.Select(d => new DepartmentView(d.Id, d.Name, d.ManagerId))], total);
    }
}

public sealed record DepartmentView(Guid DepartmentId, string Name, Guid? ManagerId);

public sealed class CreateDepartment(IHrStore store, IAuditTrail audit)
{
    public async Task<Guid> ExecuteAsync(
        string name,
        Guid? managerId,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var department = Department.Create(name, managerId);

        await store.AddDepartmentAsync(department, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.DepartmentCreated,
                HrAuditEntityTypes.Department,
                department.Id.ToString(),
                context),
            cancellationToken);

        return department.Id;
    }
}

// --- Cargos ---

public sealed class ListPositions(IHrStore store)
{
    public async Task<(IReadOnlyList<PositionView> Items, int? TotalCount)> ExecuteAsync(
        PageRequest? pagina, CancellationToken cancellationToken)
    {
        var (positions, total) = await store.ListPositionsAsync(pagina, cancellationToken);

        return ([.. positions.Select(p => new PositionView(
            p.Id, p.Name, p.HierarchyLevel, p.GrantsApprovalAuthority))], total);
    }
}

public sealed record PositionView(
    Guid PositionId,
    string Name,
    int HierarchyLevel,
    bool GrantsApprovalAuthority);

/// <summary>
/// Cria um Cargo no catálogo.
///
/// Protegido por <c>hr.positions.write</c>, que só o Admin tem (ADR-015):
/// quem controla a marca de autoridade controla, indirectamente, quem pode vir
/// a aprovar pagamentos.
/// </summary>
public sealed class CreatePosition(IHrStore store, IAuditTrail audit)
{
    public async Task<Guid> ExecuteAsync(
        string name,
        int hierarchyLevel,
        bool grantsApprovalAuthority,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        var position = Position.Create(name, hierarchyLevel, grantsApprovalAuthority);

        await store.AddPositionAsync(position, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        // A marca vai no valor registado: é o que a auditoria precisa de saber
        // para reconstituir quem criou um cargo com autoridade (BR-21).
        await audit.RecordAsync(
            new AuditRecord(
                HrAuditActions.PositionCreated,
                HrAuditEntityTypes.Position,
                position.Id.ToString(),
                context,
                NewValue: $$"""{"name":"{{position.Name}}","grantsApprovalAuthority":{{(grantsApprovalAuthority ? "true" : "false")}}}"""),
            cancellationToken);

        return position.Id;
    }
}
