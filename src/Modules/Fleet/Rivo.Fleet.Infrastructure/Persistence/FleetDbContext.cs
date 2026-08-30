using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Fleet.Domain;

namespace Rivo.Fleet.Infrastructure.Persistence;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public const string Schema = "fleet";

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

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
        });

        builder.Entity<MaintenanceRecord>(maintenance =>
        {
            maintenance.ToTable("maintenance_record");
            maintenance.HasKey(m => m.Id);

            maintenance.Property(m => m.Version).IsConcurrencyToken();

            maintenance.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            maintenance.Property(m => m.Description).HasMaxLength(500).IsRequired();

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
