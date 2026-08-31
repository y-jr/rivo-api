using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Inventory.Domain;

namespace Rivo.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<InventoryItem>(item =>
        {
            item.ToTable("item");
            item.HasKey(i => i.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            item.Property(i => i.Version).IsConcurrencyToken();

            item.Property(i => i.Sku).HasMaxLength(50).IsRequired();
            item.Property(i => i.Name).HasMaxLength(200).IsRequired();
            item.Property(i => i.Unit).HasMaxLength(20).IsRequired();
            item.Property(i => i.QuantityOnHand).HasPrecision(18, 4);
            item.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

            item.HasIndex(i => i.Sku).IsUnique();

            // Movimento é parte do agregado: não tem vida sem o item, e por
            // isso a cascata é a semântica correcta — mesma forma de
            // `Project.Milestones`/`Vehicle.Maintenances`.
            item.HasMany(i => i.Movements)
                .WithOne()
                .HasForeignKey(m => m.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            item.Navigation(i => i.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<StockMovement>(movement =>
        {
            movement.ToTable("stock_movement");
            movement.HasKey(m => m.Id);

            movement.Property(m => m.Version).IsConcurrencyToken();

            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.Quantity).HasPrecision(18, 4);
            movement.Property(m => m.Reason).HasMaxLength(500);

            movement.HasIndex(m => m.ItemId);
            movement.HasIndex(m => m.WarehouseId);

            // FK dentro do mesmo módulo (inventory → inventory): permitida
            // sem restrição de ADR-010, que só limita FK entre módulos.
            movement.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(m => m.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            movement.HasOne<Warehouse>()
                .WithMany()
                .HasForeignKey(m => m.RelatedWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Warehouse>(warehouse =>
        {
            warehouse.ToTable("warehouse");
            warehouse.HasKey(w => w.Id);

            warehouse.Property(w => w.Version).IsConcurrencyToken();

            warehouse.Property(w => w.Code).HasMaxLength(20).IsRequired();
            warehouse.Property(w => w.Name).HasMaxLength(200).IsRequired();
            warehouse.Property(w => w.Status).HasConversion<string>().HasMaxLength(20);

            warehouse.HasIndex(w => w.Code).IsUnique();
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
    /// O domínio nunca lhe toca — ver a nota em <c>InventoryItem.Version</c>.
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
