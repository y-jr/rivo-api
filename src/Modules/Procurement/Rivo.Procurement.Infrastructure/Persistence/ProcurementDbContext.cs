using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Infrastructure.Persistence;

public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public const string Schema = "procurement";

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<Supplier>(supplier =>
        {
            supplier.ToTable("supplier");
            supplier.HasKey(s => s.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            supplier.Property(s => s.Version).IsConcurrencyToken();

            supplier.Property(s => s.Name).HasMaxLength(200).IsRequired();
            supplier.Property(s => s.TaxId).HasMaxLength(30).IsRequired();

            // 34 é o máximo da norma ISO 13616.
            supplier.Property(s => s.Iban).HasMaxLength(34);
            supplier.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            supplier.Property(s => s.Email).HasMaxLength(200);
            supplier.Property(s => s.Phone).HasMaxLength(50);

            // A segunda linha da unicidade do NIF. A primeira é a verificação
            // em RegisterSupplier, que não basta: duas chamadas simultâneas
            // passam as duas por lá, e é aqui que uma perde.
            supplier.HasIndex(s => s.TaxId).IsUnique();

            supplier.HasIndex(s => s.Name);
        });

        builder.Entity<PurchaseRequisition>(requisition =>
        {
            requisition.ToTable("purchase_requisition");
            requisition.HasKey(r => r.Id);

            requisition.Property(r => r.Version).IsConcurrencyToken();

            requisition.Property(r => r.Justification).HasMaxLength(1000).IsRequired();
            requisition.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            requisition.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            requisition.Property(r => r.ClosingReason).HasMaxLength(500);

            // Sem FK para `hr.employee` nem para `approval.approval_request`:
            // são identificadores de outro contexto, e uma FK entre schemas
            // acopla o ciclo de vida dos dois lados (ADR-010). Índice sim — é
            // por aqui que se listam as requisições de uma pessoa.
            requisition.HasIndex(r => r.RequestedByEmployeeId);
            requisition.HasIndex(r => r.Status);
            requisition.HasIndex(r => r.ApprovalRequestId);

            // As linhas são parte do agregado: não têm vida sem a requisição, e
            // por isso a cascata é a semântica correcta.
            requisition.HasMany(r => r.Lines)
                .WithOne()
                .HasForeignKey(l => l.RequisitionId)
                .OnDelete(DeleteBehavior.Cascade);

            requisition.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<RequisitionLine>(line =>
        {
            line.ToTable("requisition_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.Description).HasMaxLength(500).IsRequired();

            // 18,2 é o que `finance` usa para valores monetários. Manter a mesma
            // precisão evita arredondamentos diferentes ao longo da cadeia
            // requisição → ordem de compra → factura.
            line.Property(l => l.Quantity).HasPrecision(18, 4);
            line.Property(l => l.EstimatedUnitPrice).HasPrecision(18, 2);

            // `EstimatedTotal` é calculado — não tem coluna. Guardá-lo abriria
            // a hipótese de a soma gravada discordar das parcelas.
            line.Ignore(l => l.EstimatedTotal);
        });

        // `EstimatedTotal` da requisição também é derivado, das linhas.
        builder.Entity<PurchaseRequisition>().Ignore(r => r.EstimatedTotal);
        builder.Entity<PurchaseRequisition>().Ignore(r => r.IsEditable);

        // As chaves são geradas pelo domínio (Guid.CreateVersion7), nunca pela
        // base de dados. Ver a nota longa em ApprovalDbContext.
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
    /// O domínio nunca lhe toca — ver a nota em <c>Supplier.Version</c>.
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
