using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Projects.Domain;

namespace Rivo.Projects.Infrastructure.Persistence;

public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    public const string Schema = "projects";

    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<Project>(project =>
        {
            project.ToTable("project");
            project.HasKey(p => p.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            project.Property(p => p.Version).IsConcurrencyToken();

            project.Property(p => p.Name).HasMaxLength(200).IsRequired();
            project.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

            project.HasIndex(p => p.Name);

            // Marco e Tarefa são parte do agregado: não têm vida sem o
            // projecto, e por isso a cascata é a semântica correcta — mesma
            // forma de `PurchaseRequisition.Lines` em `procurement`.
            project.HasMany(p => p.Milestones)
                .WithOne()
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            project.Navigation(p => p.Milestones).UsePropertyAccessMode(PropertyAccessMode.Field);

            project.HasMany(p => p.Tasks)
                .WithOne()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            project.Navigation(p => p.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Zero ou um por projecto, ao contrário de Marco e Tarefa — mesma
            // cascata, porque continua a ser parte do agregado.
            project.HasOne(p => p.Budget)
                .WithOne()
                .HasForeignKey<ProjectBudget>(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            project.Navigation(p => p.Budget).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Alocação de Recursos: parte do agregado, mesma cascata de
            // Marco e Tarefa. Várias abertas ao mesmo tempo são normais —
            // um projecto tem vários recursos alocados em simultâneo.
            project.HasMany(p => p.Allocations)
                .WithOne()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            project.Navigation(p => p.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<Milestone>(milestone =>
        {
            milestone.ToTable("milestone");
            milestone.HasKey(m => m.Id);

            milestone.Property(m => m.Version).IsConcurrencyToken();

            milestone.Property(m => m.Name).HasMaxLength(200).IsRequired();
            milestone.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

            milestone.HasIndex(m => m.ProjectId);
        });

        builder.Entity<ProjectTask>(task =>
        {
            task.ToTable("project_task");
            task.HasKey(t => t.Id);

            task.Property(t => t.Version).IsConcurrencyToken();

            task.Property(t => t.Title).HasMaxLength(200).IsRequired();
            task.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

            task.HasIndex(t => t.ProjectId);

            // Sem FK para `hr.employee`: é identificador de outro contexto, e
            // uma FK entre schemas acopla o ciclo de vida dos dois lados
            // (ADR-010). Índice sim — é por aqui que se listam as tarefas de
            // uma pessoa.
            task.HasIndex(t => t.AssignedEmployeeId);
        });

        builder.Entity<ProjectBudget>(budget =>
        {
            budget.ToTable("project_budget");
            budget.HasKey(b => b.Id);

            budget.Property(b => b.Version).IsConcurrencyToken();

            budget.Property(b => b.Amount).HasPrecision(18, 2);
            budget.Property(b => b.Currency).HasMaxLength(3).IsRequired();

            budget.HasIndex(b => b.ProjectId).IsUnique();
        });

        builder.Entity<ProjectResourceAllocation>(allocation =>
        {
            allocation.ToTable("project_resource_allocation");
            allocation.HasKey(a => a.Id);

            allocation.Property(a => a.Version).IsConcurrencyToken();
            allocation.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);

            allocation.HasIndex(a => a.ProjectId);

            // Sem FK para hr.employee nem fleet.vehicle: são identificadores
            // de outros contextos, e uma FK entre schemas acoplaria o ciclo
            // de vida dos dois lados (ADR-010) — mesma razão de
            // ProjectTask.AssignedEmployeeId. Índice composto sim: é por
            // aqui que se verifica se um recurso já está alocado.
            allocation.HasIndex(a => new { a.Kind, a.ResourceId });
        });

        // As chaves são geradas pelo domínio (Guid.CreateVersion7), nunca pela
        // base de dados.
        foreach (var key in builder.Model.GetEntityTypes()
                     .Select(entity => entity.FindPrimaryKey())
                     .SelectMany(primaryKey => primaryKey?.Properties ?? [])
                     .Where(property => property.ClrType == typeof(Guid)))
        {
            key.ValueGenerated = ValueGenerated.Never;
        }
    }

    /// <summary>
    /// Incrementa o contador de concorrência de tudo o que vai ser alterado.
    /// O domínio nunca lhe toca — ver a nota em <c>Project.Version</c>.
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
