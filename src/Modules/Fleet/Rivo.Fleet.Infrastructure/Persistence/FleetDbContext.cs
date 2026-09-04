using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Infrastructure.Persistence;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public const string Schema = "fleet";

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<VehicleDocument> VehicleDocuments => Set<VehicleDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<Vehicle>(vehicle =>
        {
            vehicle.ToTable("vehicle");
            vehicle.HasKey(v => v.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            vehicle.Property(v => v.Version).IsConcurrencyToken();

            vehicle.Property(v => v.PlateNumber).HasMaxLength(20).IsRequired();
            vehicle.Property(v => v.Model).HasMaxLength(100).IsRequired();
            vehicle.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);

            vehicle.HasIndex(v => v.PlateNumber).IsUnique();

            // Manutenção e Atribuição são parte do agregado: não têm vida sem
            // a viatura, e por isso a cascata é a semântica correcta — mesma
            // forma de `Project.Milestones`/`Project.Tasks` em `projects`.
            vehicle.HasMany(v => v.Maintenances)
                .WithOne()
                .HasForeignKey(m => m.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);

            vehicle.Navigation(v => v.Maintenances).UsePropertyAccessMode(PropertyAccessMode.Field);

            vehicle.HasMany(v => v.Assignments)
                .WithOne()
                .HasForeignKey(a => a.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);

            vehicle.Navigation(v => v.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

            vehicle.HasMany(v => v.Plans)
                .WithOne()
                .HasForeignKey(p => p.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);

            vehicle.Navigation(v => v.Plans).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Registo de Viagem e Despesa de Frota são parte do agregado,
            // mesma forma de Manutenção/Atribuição/Plano acima.
            vehicle.HasMany(v => v.Trips)
                .WithOne()
                .HasForeignKey(t => t.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);

            vehicle.Navigation(v => v.Trips).UsePropertyAccessMode(PropertyAccessMode.Field);

            vehicle.HasMany(v => v.Expenses)
                .WithOne()
                .HasForeignKey(e => e.VehicleId)
                .OnDelete(DeleteBehavior.Cascade);

            vehicle.Navigation(v => v.Expenses).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<MaintenanceRecord>(maintenance =>
        {
            maintenance.ToTable("maintenance_record");
            maintenance.HasKey(m => m.Id);

            maintenance.Property(m => m.Version).IsConcurrencyToken();

            maintenance.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            maintenance.Property(m => m.Description).HasMaxLength(500).IsRequired();
            maintenance.Property(m => m.Cost).HasPrecision(18, 2);

            maintenance.HasIndex(m => m.VehicleId);
        });

        builder.Entity<VehicleAssignment>(assignment =>
        {
            assignment.ToTable("vehicle_assignment");
            assignment.HasKey(a => a.Id);

            assignment.Property(a => a.Version).IsConcurrencyToken();

            assignment.HasIndex(a => a.VehicleId);

            // Sem FK para `hr.employee`: é identificador de outro contexto, e
            // uma FK entre schemas acopla o ciclo de vida dos dois lados
            // (ADR-010). Índice sim — é por aqui que se listam as atribuições
            // de um motorista.
            assignment.HasIndex(a => a.EmployeeId);
        });

        builder.Entity<MaintenancePlan>(plan =>
        {
            plan.ToTable("maintenance_plan");
            plan.HasKey(p => p.Id);

            plan.Property(p => p.Version).IsConcurrencyToken();

            plan.Property(p => p.Description).HasMaxLength(500).IsRequired();

            plan.HasIndex(p => p.VehicleId);

            // É por aqui que a consulta de "alerta" filtra — ver
            // `IVehicleStore.ListWithDuePlansAsync`.
            plan.HasIndex(p => new { p.IsActive, p.NextDueOn });
        });

        builder.Entity<VehicleTrip>(trip =>
        {
            trip.ToTable("vehicle_trip");
            trip.HasKey(t => t.Id);

            trip.Property(t => t.Version).IsConcurrencyToken();

            trip.Property(t => t.StartOdometer).HasPrecision(18, 2);
            trip.Property(t => t.EndOdometer).HasPrecision(18, 2);
            trip.Property(t => t.Purpose).HasMaxLength(500);
            trip.Ignore(t => t.Distance);

            trip.HasIndex(t => t.VehicleId);

            // Sem FK para `hr.employee` — mesma nota de `VehicleAssignment`
            // acima (ADR-010). Índice sim, para listar as viagens de um
            // motorista.
            trip.HasIndex(t => t.DriverId);
        });

        builder.Entity<FleetExpense>(expense =>
        {
            expense.ToTable("fleet_expense");
            expense.HasKey(e => e.Id);

            expense.Property(e => e.Version).IsConcurrencyToken();

            expense.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
            expense.Property(e => e.Amount).HasPrecision(18, 2);
            expense.Property(e => e.Description).HasMaxLength(500);

            expense.HasIndex(e => e.VehicleId);
        });

        builder.Entity<VehicleDocument>(link =>
        {
            link.ToTable("vehicle_document");
            link.HasKey(l => l.Id);
            link.Property(l => l.Category).HasMaxLength(100).IsRequired();

            link.HasOne<Vehicle>()
                .WithMany()
                .HasForeignKey(l => l.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            // Chave estrangeira para documents.document(id) declarada por SQL
            // na migração seguinte, e não por navegação de EF: a entidade
            // Document pertence a `documents` e não pode ser referenciada a
            // partir daqui (ADR-017) — mesma nota de `EmployeeDocument` em `hr`.
            link.HasIndex(l => l.VehicleId);
            link.HasIndex(l => l.DocumentId).IsUnique();
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
    /// O domínio nunca lhe toca — ver a nota em <c>Vehicle.Version</c>.
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
