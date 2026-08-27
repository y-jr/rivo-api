using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Procurement.Domain;

namespace Rivo.Procurement.Infrastructure.Persistence;

public sealed class ProcurementDbContext(DbContextOptions<ProcurementDbContext> options) : DbContext(options)
{
    public const string Schema = "procurement";

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();

    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();

    public DbSet<GoodsReceipt> Receipts => Set<GoodsReceipt>();

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

        builder.Entity<PurchaseOrder>(order =>
        {
            order.ToTable("purchase_order");
            order.HasKey(o => o.Id);

            order.Property(o => o.Version).IsConcurrencyToken();

            order.Property(o => o.Currency).HasMaxLength(3).IsRequired();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.CancellationReason).HasMaxLength(500);

            // FK real para a requisição: as duas vivem no mesmo schema e no
            // mesmo módulo, e uma ordem sem a requisição que a autorizou não
            // teria sentido nenhum. **Sem cascata** — apagar uma requisição
            // levaria atrás encomendas que saíram para fornecedores (BR-14).
            order.HasOne<PurchaseRequisition>()
                .WithMany()
                .HasForeignKey(o => o.RequisitionId)
                .OnDelete(DeleteBehavior.Restrict);

            // O fornecedor também é deste schema, e a restrição impede
            // desqualificar por eliminação o que tem encomendas em curso.
            order.HasOne<Supplier>()
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            order.HasIndex(o => o.RequisitionId);
            order.HasIndex(o => o.SupplierId);
            order.HasIndex(o => o.Status);

            order.HasMany(o => o.Lines)
                .WithOne()
                .HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<PurchaseOrderLine>(line =>
        {
            line.ToTable("purchase_order_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.Description).HasMaxLength(500).IsRequired();
            line.Property(l => l.Quantity).HasPrecision(18, 4);
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);

            line.Ignore(l => l.LineTotal);
        });

        builder.Entity<GoodsReceipt>(receipt =>
        {
            receipt.ToTable("goods_receipt");
            receipt.HasKey(g => g.Id);

            receipt.Property(g => g.Version).IsConcurrencyToken();

            receipt.Property(g => g.DeliveryNote).HasMaxLength(60);
            receipt.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
            receipt.Property(g => g.CancellationReason).HasMaxLength(500);

            // Sem cascata: anular a ordem não apaga o registo de que a
            // mercadoria chegou (BR-14).
            receipt.HasOne<PurchaseOrder>()
                .WithMany()
                .HasForeignKey(g => g.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Sem FK para `hr.employee`: identificador de outro contexto
            // (ADR-010).
            receipt.HasIndex(g => g.PurchaseOrderId);
            receipt.HasIndex(g => g.Status);
            receipt.HasIndex(g => g.ReceivedByEmployeeId);

            receipt.HasMany(g => g.Lines)
                .WithOne()
                .HasForeignKey(l => l.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);

            receipt.Navigation(g => g.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<GoodsReceiptLine>(line =>
        {
            line.ToTable("goods_receipt_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.QuantityReceived).HasPrecision(18, 4);

            // FK para a linha da ordem: é por ela que o 3-way match compara o
            // que chegou com o que se pediu. **Restrict**, pela mesma razão de
            // sempre — a contagem é facto, e não se apaga com a encomenda.
            line.HasOne<PurchaseOrderLine>()
                .WithMany()
                .HasForeignKey(l => l.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);

            line.HasIndex(l => l.PurchaseOrderLineId);
        });

        builder.Entity<PurchaseOrder>().Ignore(o => o.Total);

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
