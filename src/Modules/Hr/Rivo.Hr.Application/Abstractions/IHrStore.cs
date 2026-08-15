using Rivo.Hr.Domain;

namespace Rivo.Hr.Application.Abstractions;

/// <summary>
/// Persistência do módulo. Definida aqui e implementada em Infrastructure,
/// para que os casos de uso não conheçam o EF Core.
///
/// Uma interface para o módulo inteiro, e não uma por agregado: os agregados
/// de `hr` são consultados em conjunto (um colaborador com o seu cargo e
/// departamento) e separá-los agora seria cerimónia sem benefício.
/// </summary>
public interface IHrStore
{
    // --- Colaboradores ---

    Task<Employee?> FindEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Employee>> ListEmployeesAsync(CancellationToken cancellationToken);

    Task AddEmployeeAsync(Employee employee, CancellationToken cancellationToken);

    // --- Departamentos ---

    Task<bool> DepartmentExistsAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Department>> ListDepartmentsAsync(CancellationToken cancellationToken);

    Task AddDepartmentAsync(Department department, CancellationToken cancellationToken);

    // --- Cargos ---

    Task<Position?> FindPositionAsync(Guid positionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Position>> ListPositionsAsync(CancellationToken cancellationToken);

    Task AddPositionAsync(Position position, CancellationToken cancellationToken);

    // --- Atribuições de cargo ---

    Task AddAssignmentAsync(PositionAssignment assignment, CancellationToken cancellationToken);

    /// <summary>Atribuições de um colaborador, para resolver o cargo à data.</summary>
    Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Atribuições de um cargo, para resolver quem o ocupa à data.</summary>
    Task<IReadOnlyList<PositionAssignment>> ListAssignmentsForPositionAsync(Guid positionId, CancellationToken cancellationToken);

    // --- Documentos anexados (ADR-009: a ligação vive aqui, não em `documents`) ---

    Task AddEmployeeDocumentAsync(EmployeeDocument link, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeDocument>> ListEmployeeDocumentsAsync(Guid employeeId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
