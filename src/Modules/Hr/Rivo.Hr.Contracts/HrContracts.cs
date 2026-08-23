namespace Rivo.Hr.Contracts;

/// <summary>
/// Superfície publicada de `hr`. Assembly sem dependências (ADR-017).
///
/// <para>
/// O contrato central é <see cref="EmployeeReference"/>: `hr.Colaborador` é a
/// entidade com maior fan-out do sistema, e o acesso externo é estritamente
/// contratual para que o seu modelo interno possa evoluir sem raio de impacto
/// global (ADR-010).
/// </para>
/// </summary>
public interface IEmployeeDirectory
{
    /// <summary>
    /// Referência a um colaborador. Os consumidores guardam apenas o
    /// identificador e lêem os atributos por aqui — nunca copiam nome,
    /// departamento ou cargo para as suas tabelas, porque essas cópias ficam
    /// obsoletas em silêncio (BR-18).
    /// </summary>
    Task<EmployeeReference?> FindAsync(Guid employeeId, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Quem ocupa um Cargo a determinada data. É o que `approval` usará para
    /// resolver aprovadores.
    ///
    /// A data importa: as atribuições são históricas, e um processo submetido
    /// em Março tem de resolver quem ocupava o Cargo em Março (BR-6).
    /// </summary>
    Task<IReadOnlyList<EmployeeReference>> FindByPositionAsync(Guid positionId, DateTimeOffset asOf, CancellationToken cancellationToken);
}

/// <param name="UserId">
/// Opcional: nem todo o colaborador tem login, e nem todo o utilizador é
/// colaborador (ADR-004).
/// </param>
/// <param name="CurrentPosition">Cargo efectivo à data pedida. Nulo se não ocupar nenhum.</param>
public sealed record EmployeeReference(
    Guid EmployeeId,
    string DisplayName,
    EmployeeStatus Status,
    Guid? DepartmentId,
    PositionReference? CurrentPosition,
    Guid? UserId);

public sealed record PositionReference(Guid PositionId, string Name, bool GrantsApprovalAuthority);

public enum EmployeeStatus
{
    Active,
    Inactive,
}

/// <summary>Catálogo de permissões de `hr`, declarado pelo próprio módulo.</summary>
public static class HrPermissions
{
    public const string EmployeesRead = "hr.employees.read";
    public const string EmployeesWrite = "hr.employees.write";
    public const string DepartmentsRead = "hr.departments.read";
    public const string DepartmentsWrite = "hr.departments.write";
    public const string PositionsRead = "hr.positions.read";

    /// <summary>
    /// Gerir o catálogo de Cargos, incluindo a marca que confere autoridade de
    /// aprovação. **Apenas Admin** (ADR-015): quem controla a marca controla,
    /// indirectamente, quem pode vir a aprovar pagamentos.
    /// </summary>
    public const string PositionsWrite = "hr.positions.write";

    /// <summary>Atribuir Cargos a colaboradores. Operação corrente de RH (ADR-015).</summary>
    public const string PositionsAssign = "hr.positions.assign";

    /// <summary>
    /// Ler contratos de trabalho, incluindo a remuneração acordada.
    ///
    /// <para>
    /// <strong>Separada de <see cref="EmployeesRead"/> de propósito.</strong>
    /// Ver quem trabalha na empresa e ver quanto cada um ganha não são a mesma
    /// autorização — juntá-las daria o salário de toda a gente a quem só
    /// precisa do organograma.
    /// </para>
    /// </summary>
    public const string ContractsRead = "hr.contracts.read";

    /// <summary>Celebrar e cessar contratos de trabalho.</summary>
    public const string ContractsWrite = "hr.contracts.write";

    /// <summary>Consultar assiduidade, incluindo a de terceiros.</summary>
    public const string AttendanceRead = "hr.attendance.read";

    /// <summary>
    /// Marcar ponto, registar e justificar faltas.
    ///
    /// <para>
    /// Distinta da leitura porque são audiências diferentes: marcar ponto é
    /// operação de quem trabalha, consultar a assiduidade alheia é de quem
    /// gere.
    /// </para>
    /// </summary>
    public const string AttendanceWrite = "hr.attendance.write";

    /// <summary>Catálogo de benefícios e adesões.</summary>
    public const string BenefitsRead = "hr.benefits.read";

    public const string BenefitsWrite = "hr.benefits.write";

    /// <summary>Vagas e candidatos.</summary>
    public const string RecruitmentRead = "hr.recruitment.read";

    public const string RecruitmentWrite = "hr.recruitment.write";

    /// <summary>Processos de entrada e de saída, e as respectivas checklists.</summary>
    public const string LifecycleRead = "hr.lifecycle.read";

    public const string LifecycleWrite = "hr.lifecycle.write";

    public static readonly IReadOnlyList<string> All =
    [
        ContractsRead,
        ContractsWrite,
        AttendanceRead,
        AttendanceWrite,
        BenefitsRead,
        BenefitsWrite,
        RecruitmentRead,
        RecruitmentWrite,
        LifecycleRead,
        LifecycleWrite,
        EmployeesRead,
        EmployeesWrite,
        DepartmentsRead,
        DepartmentsWrite,
        PositionsRead,
        PositionsWrite,
        PositionsAssign,
    ];

    /// <summary>
    /// O que o perfil HR recebe. Note-se a ausência de
    /// <see cref="PositionsWrite"/>: RH atribui Cargos, mas não decide quais
    /// existem nem quais conferem autoridade.
    /// </summary>
    public static readonly IReadOnlyList<string> ForHumanResources =
    [
        EmployeesRead,
        EmployeesWrite,
        DepartmentsRead,
        DepartmentsWrite,
        PositionsRead,
        PositionsAssign,
        ContractsRead,
        ContractsWrite,
        AttendanceRead,
        AttendanceWrite,
        BenefitsRead,
        BenefitsWrite,
        RecruitmentRead,
        RecruitmentWrite,
        LifecycleRead,
        LifecycleWrite,
    ];
}
