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

    public DbSet<EmploymentContract> EmploymentContracts => Set<EmploymentContract>();

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<Benefit> Benefits => Set<Benefit>();

    public DbSet<BenefitEnrolment> BenefitEnrolments => Set<BenefitEnrolment>();

    public DbSet<JobOpening> JobOpenings => Set<JobOpening>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<EmployeeLifecycleProcess> LifecycleProcesses => Set<EmployeeLifecycleProcess>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Employee>(employee =>
        {
            employee.ToTable("employee");
            employee.HasKey(e => e.Id);
            // Concorrência optimista (ADR-002, ADR-025).
            employee.Property(e => e.Version).IsConcurrencyToken();
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
            // Concorrência optimista (ADR-002, ADR-025).
            department.Property(d => d.Version).IsConcurrencyToken();
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
            // Concorrência optimista (ADR-002, ADR-025).
            assignment.Property(a => a.Version).IsConcurrencyToken();
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

        builder.Entity<EmploymentContract>(contract =>
        {
            contract.ToTable("employment_contract");
            contract.HasKey(c => c.Id);
            // Concorrência optimista (ADR-002, ADR-025).
            contract.Property(c => c.Version).IsConcurrencyToken();
            contract.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
            contract.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

            // Precisão explícita, nunca vírgula flutuante para dinheiro
            // (standards/persistence.md). 18,2 acomoda folgadamente salários em
            // kwanzas sem perder cêntimos.
            contract.Property(c => c.MonthlySalary).HasPrecision(18, 2);

            // ISO 4217 tem exactamente três letras.
            contract.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            contract.Property(c => c.Notes).HasMaxLength(1000);

            // A consulta corrente: os contratos de uma pessoa, do mais recente
            // para trás — e é sobre ela que corre a verificação de vigências
            // sobrepostas antes de celebrar um novo.
            contract.HasIndex(c => new { c.EmployeeId, c.StartsOn });

            contract.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(c => c.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AttendanceRecord>(attendance =>
        {
            attendance.ToTable("attendance_record");
            attendance.HasKey(a => a.Id);
            // Concorrência optimista (ADR-002, ADR-025).
            attendance.Property(a => a.Version).IsConcurrencyToken();
            attendance.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            attendance.Property(a => a.Justification).HasMaxLength(500);

            // Um registo por colaborador e por dia, imposto pela base de dados.
            //
            // A verificação no caso de uso apanha o caso normal; só o índice
            // único apanha duas marcações simultâneas do mesmo dia — que é
            // precisamente o que um relógio de ponto com rede instável produz.
            attendance.HasIndex(a => new { a.EmployeeId, a.Day }).IsUnique();

            // A consulta da fila de RH: anomalias dos últimos dias, para toda
            // a empresa.
            attendance.HasIndex(a => new { a.Day, a.Status });

            attendance.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Benefit>(benefit =>
        {
            benefit.ToTable("benefit");
            benefit.HasKey(b => b.Id);
            benefit.Property(b => b.Version).IsConcurrencyToken();
            benefit.Property(b => b.Name).HasMaxLength(200).IsRequired();
            benefit.Property(b => b.Kind).HasMaxLength(50).IsRequired();
            benefit.Property(b => b.Currency).HasMaxLength(3).IsRequired();
            benefit.Property(b => b.Description).HasMaxLength(1000);
            benefit.Property(b => b.MonthlyValue).HasPrecision(18, 2);
            benefit.HasIndex(b => b.Name).IsUnique();
        });

        builder.Entity<BenefitEnrolment>(enrolment =>
        {
            enrolment.ToTable("benefit_enrolment");
            enrolment.HasKey(e => e.Id);
            enrolment.Property(e => e.Version).IsConcurrencyToken();
            enrolment.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            enrolment.HasIndex(e => new { e.EmployeeId, e.BenefitId });

            enrolment.HasOne<Employee>().WithMany()
                .HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);

            enrolment.HasOne<Benefit>().WithMany()
                .HasForeignKey(e => e.BenefitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<JobOpening>(opening =>
        {
            opening.ToTable("job_opening");
            opening.HasKey(o => o.Id);
            opening.Property(o => o.Version).IsConcurrencyToken();
            opening.Property(o => o.Title).HasMaxLength(200).IsRequired();
            opening.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            opening.Property(o => o.Description).HasMaxLength(2000);
            opening.Property(o => o.Requirements).HasMaxLength(2000);
            opening.HasIndex(o => o.Status);

            // Sem FK para department: a vaga pode existir antes de o
            // departamento estar criado, e o campo é opcional.
            opening.HasIndex(o => o.DepartmentId);
        });

        builder.Entity<Candidate>(candidate =>
        {
            candidate.ToTable("candidate");
            candidate.HasKey(c => c.Id);
            candidate.Property(c => c.Version).IsConcurrencyToken();
            candidate.Property(c => c.FullName).HasMaxLength(200).IsRequired();
            candidate.Property(c => c.Email).HasMaxLength(320);
            candidate.Property(c => c.Phone).HasMaxLength(30);
            candidate.Property(c => c.Notes).HasMaxLength(2000);
            candidate.Property(c => c.Stage).HasConversion<string>().HasMaxLength(20);

            // A consulta do funil: candidatos de uma vaga, por fase.
            candidate.HasIndex(c => new { c.JobOpeningId, c.Stage });

            candidate.HasOne<JobOpening>().WithMany()
                .HasForeignKey(c => c.JobOpeningId).OnDelete(DeleteBehavior.Restrict);

            // Sem FK para employee: o vínculo só existe depois de contratado, e
            // o candidato é histórico que sobrevive à saída dessa pessoa.
            candidate.HasIndex(c => c.HiredEmployeeId);
        });

        builder.Entity<EmployeeLifecycleProcess>(process =>
        {
            process.ToTable("lifecycle_process");
            process.HasKey(p => p.Id);
            process.Property(p => p.Version).IsConcurrencyToken();
            process.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
            process.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            process.Property(p => p.Reason).HasMaxLength(500);
            process.Property(p => p.Notes).HasMaxLength(2000);
            process.HasIndex(p => new { p.Kind, p.Status });

            process.HasOne<Employee>().WithMany()
                .HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Restrict);

            // As tarefas são parte do agregado, não entidade independente:
            // acedem-se pelo processo e morrem com ele. Daí o campo de apoio e
            // o `Cascade` — ao contrário de todas as outras relações aqui.
            process.HasMany(p => p.Tasks)
                .WithOne()
                .HasForeignKey(t => t.ProcessId)
                .OnDelete(DeleteBehavior.Cascade);

            process.Navigation(p => p.Tasks)
                .HasField("_tasks")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<LifecycleTask>(task =>
        {
            task.ToTable("lifecycle_task");
            task.HasKey(t => t.Id);
            task.Property(t => t.Version).IsConcurrencyToken();
            task.Property(t => t.Title).HasMaxLength(200).IsRequired();
            task.Property(t => t.Category).HasMaxLength(50).IsRequired();
            task.Property(t => t.Description).HasMaxLength(1000);
            task.HasIndex(t => new { t.ProcessId, t.Order });
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

    /// <summary>
    /// Incrementa o contador de concorrência de tudo o que vai ser alterado.
    ///
    /// <para>
    /// O domínio nunca mexe no <c>Version</c>: obrigá-lo a lembrar-se disso em
    /// cada método que altera estado seria uma regra que se esquece uma vez e
    /// falha em silêncio para sempre. Aqui é impossível esquecer.
    /// </para>
    ///
    /// <para>
    /// Subir o <c>CurrentValue</c> basta — o EF Core usa o <c>OriginalValue</c>
    /// na cláusula <c>WHERE</c>, que é o que detecta a escrita concorrente.
    /// </para>
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Version");

            if (version?.CurrentValue is int current)
            {
                version.CurrentValue = current + 1;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
