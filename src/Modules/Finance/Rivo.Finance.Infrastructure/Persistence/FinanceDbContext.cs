using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    public DbSet<DocumentSeries> Series => Set<DocumentSeries>();

    public DbSet<SalesInvoice> Invoices => Set<SalesInvoice>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<DocumentSeries>(series =>
        {
            series.ToTable("document_series");
            series.HasKey(s => s.Id);

            // Não é formalidade: é o que impede duas emissões simultâneas de
            // receberem o mesmo número (ADR-025, ADR-035).
            series.Property(s => s.Version).IsConcurrencyToken();

            series.Property(s => s.Type).HasConversion<string>().HasMaxLength(5);
            series.Property(s => s.Code).HasMaxLength(20).IsRequired();

            series.HasIndex(s => new { s.Type, s.Code }).IsUnique();
        });

        builder.Entity<SalesInvoice>(invoice =>
        {
            invoice.ToTable("sales_invoice");
            invoice.HasKey(i => i.Id);
            invoice.Property(i => i.Version).IsConcurrencyToken();

            invoice.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            invoice.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            invoice.Property(i => i.CancellationReason).HasMaxLength(500);

            // Menção de não-validade fiscal, congelada na emissão (ADR-036).
            // Anulável: nulo é o estado de um sistema certificado.
            invoice.Property(i => i.FiscalNotice).HasMaxLength(300);

            // Precisão fixada. Sem isto o SQL Server escolhe a omissão e o
            // total gravado deixa de ser exactamente o que o documento mostra.
            invoice.Property(i => i.NetTotal).HasPrecision(18, 2);
            invoice.Property(i => i.TaxTotal).HasPrecision(18, 2);
            invoice.Property(i => i.GrossTotal).HasPrecision(18, 2);

            // O número, como colunas na própria factura.
            invoice.OwnsOne(i => i.Number, number =>
            {
                number.Property(n => n.Type).HasColumnName("number_type").HasConversion<string>().HasMaxLength(5);
                number.Property(n => n.Series).HasColumnName("number_series").HasMaxLength(20).IsRequired();
                number.Property(n => n.Sequence).HasColumnName("number_sequence").IsRequired();

                // `Formatted` é uma composição das outras três, não uma coluna.
                number.Ignore(n => n.Formatted);

                // A garantia final de que não saem dois documentos com o mesmo
                // número. A primeira é o contador de concorrência da série;
                // esta é a que resiste a tudo o resto.
                number.HasIndex(n => new { n.Type, n.Series, n.Sequence }).IsUnique();
            });

            // O cliente congelado. Colunas na factura, não uma relação: é um
            // retrato do momento da emissão e não uma referência viva.
            invoice.OwnsOne(i => i.Customer, party =>
            {
                party.Property(p => p.Name).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
                party.Property(p => p.TaxId).HasColumnName("customer_tax_id").HasMaxLength(30).IsRequired();
                // Continuam `IsRequired`, e é deliberado: numa venda a consumidor
                // final a morada é **string vazia**, não nula. A distinção
                // importa — vazio é "não existe morada", e nulo seria "não
                // sabemos", que é outra coisa e não é o caso (ADR-036).
                party.Property(p => p.AddressDetail).HasColumnName("customer_address_detail").HasMaxLength(300).IsRequired();
                party.Property(p => p.City).HasColumnName("customer_city").HasMaxLength(100).IsRequired();
                party.Property(p => p.Country).HasColumnName("customer_country").HasMaxLength(2).IsRequired();
                party.Property(p => p.IsFinalConsumer).HasColumnName("customer_is_final_consumer");
            });

            // Sem chave estrangeira para `commercial.customer`: são schemas de
            // módulos distintos, e a factura referencia o cliente por
            // identificador (ADR-010).
            invoice.HasIndex(i => i.CustomerId);
            invoice.HasIndex(i => i.IssuedOn);

            invoice.HasMany(i => i.Lines)
                .WithOne()
                .HasForeignKey(l => l.SalesInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            invoice.Navigation(i => i.Lines)
                .HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<SalesInvoiceLine>(line =>
        {
            line.ToTable("sales_invoice_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.Description).HasMaxLength(300).IsRequired();
            line.Property(l => l.TaxCode).HasMaxLength(10).IsRequired();

            line.Property(l => l.Quantity).HasPrecision(18, 4);
            line.Property(l => l.UnitPrice).HasPrecision(18, 4);
            line.Property(l => l.TaxPercentage).HasPrecision(5, 2);
            line.Property(l => l.NetAmount).HasPrecision(18, 2);
            line.Property(l => l.TaxAmount).HasPrecision(18, 2);

            line.HasIndex(l => new { l.SalesInvoiceId, l.LineNumber }).IsUnique();
        });

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
    /// O domínio nunca lhe toca.
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
