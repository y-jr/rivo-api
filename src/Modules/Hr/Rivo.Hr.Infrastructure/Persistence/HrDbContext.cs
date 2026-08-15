using Microsoft.EntityFrameworkCore;
using Rivo.Hr.Domain;

namespace Rivo.Hr.Infrastructure.Persistence;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : DbContext(options)
{
    public const string Schema = "hr";

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<PositionAssignment> PositionAssignments => Set<PositionAssignment>();

    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Employee>(employee =>
        {
            employee.ToTable("employee");
            employee.HasKey(e => e.Id);
            employee.Property(e => e.FullName).HasMaxLength(200).IsRequired();
            employee.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            // Sem chave estrangeira para identity.app_user: são schemas de
            // módulos distintos, e a ligação é opcional nos dois sentidos
            // (ADR-004). Guarda-se o identificador.
            employee.HasIndex(e => e.UserId);
            employee.HasIndex(e => e.DepartmentId);
        });

        builder.Entity<Department>(department =>
        {
            department.ToTable("department");
            department.HasKey(d => d.Id);
            department.Property(d => d.Name).HasMaxLength(200).IsRequired();
            department.HasIndex(d => d.Name).IsUnique();
        });

        builder.Entity<Position>(position =>
        {
            position.ToTable("position");
            position.HasKey(p => p.Id);
            position.Property(p => p.Name).HasMaxLength(200).IsRequired();
            position.HasIndex(p => p.Name).IsUnique();

            // Consulta frequente em auditoria e revisão de segurança: que
            // cargos conferem autoridade de aprovação (BR-21).
            position.HasIndex(p => p.GrantsApprovalAuthority);
        });

        builder.Entity<PositionAssignment>(assignment =>
        {
            assignment.ToTable("position_assignment");
            assignment.HasKey(a => a.Id);
            assignment.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            // As duas consultas que `approval` fará: o cargo de uma pessoa à
            // data, e quem ocupa um cargo à data.
            assignment.HasIndex(a => new { a.EmployeeId, a.EffectiveFrom });
            assignment.HasIndex(a => new { a.PositionId, a.EffectiveFrom });

            assignment.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            assignment.HasOne<Position>()
                .WithMany()
                .HasForeignKey(a => a.PositionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmployeeDocument>(link =>
        {
            link.ToTable("employee_document");
            link.HasKey(l => l.Id);
            link.Property(l => l.Category).HasMaxLength(100).IsRequired();

            link.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(l => l.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Chave estrangeira para documents.document(id): FK entre schemas
            // para a chave primária do contexto dono, único caso permitido
            // (ADR-010). É esta FK que devolve a integridade referencial que a
            // chave polimórfica não dava (ADR-009).
            //
            // Declarada por SQL na migração, e não por navegação de EF: a
            // entidade Document pertence a `documents` e não pode ser
            // referenciada a partir daqui (ADR-017).
            link.HasIndex(l => l.EmployeeId);
            link.HasIndex(l => l.DocumentId).IsUnique();
        });
    }
}
