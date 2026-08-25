using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    public DbSet<DocumentSeries> Series => Set<DocumentSeries>();

    public DbSet<SalesInvoice> Invoices => Set<SalesInvoice>();

    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<BankAccount> Accounts => Set<BankAccount>();

    public DbSet<BankMovement> Movements => Set<BankMovement>();

    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();

    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();

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

        builder.Entity<CreditNote>(note =>
        {
            note.ToTable("credit_note");
            note.HasKey(n => n.Id);
            note.Property(n => n.Version).IsConcurrencyToken();

            note.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
            note.Property(n => n.Currency).HasMaxLength(3).IsRequired();
            note.Property(n => n.Reason).HasMaxLength(500).IsRequired();
            note.Property(n => n.CorrectedInvoiceNumber).HasMaxLength(40).IsRequired();
            note.Property(n => n.CancellationReason).HasMaxLength(500);
            note.Property(n => n.FiscalNotice).HasMaxLength(300);

            note.Property(n => n.NetTotal).HasPrecision(18, 2);
            note.Property(n => n.TaxTotal).HasPrecision(18, 2);
            note.Property(n => n.GrossTotal).HasPrecision(18, 2);

            note.OwnsOne(n => n.Number, number =>
            {
                number.Property(x => x.Type).HasColumnName("number_type").HasConversion<string>().HasMaxLength(5);
                number.Property(x => x.Series).HasColumnName("number_series").HasMaxLength(20).IsRequired();
                number.Property(x => x.Sequence).HasColumnName("number_sequence").IsRequired();
                number.Ignore(x => x.Formatted);
                number.HasIndex(x => new { x.Type, x.Series, x.Sequence }).IsUnique();
            });

            note.OwnsOne(n => n.Customer, party =>
            {
                party.Property(p => p.Name).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
                party.Property(p => p.TaxId).HasColumnName("customer_tax_id").HasMaxLength(30).IsRequired();
                party.Property(p => p.AddressDetail).HasColumnName("customer_address_detail").HasMaxLength(300).IsRequired();
                party.Property(p => p.City).HasColumnName("customer_city").HasMaxLength(100).IsRequired();
                party.Property(p => p.Country).HasColumnName("customer_country").HasMaxLength(2).IsRequired();
                party.Property(p => p.IsFinalConsumer).HasColumnName("customer_is_final_consumer");
            });

            // A consulta do saldo: as notas de uma factura.
            note.HasIndex(n => n.SalesInvoiceId);

            note.HasMany(n => n.Lines)
                .WithOne()
                .HasForeignKey(l => l.CreditNoteId)
                .OnDelete(DeleteBehavior.Cascade);

            note.Navigation(n => n.Lines)
                .HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<CreditNoteLine>(line =>
        {
            line.ToTable("credit_note_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.Description).HasMaxLength(300).IsRequired();
            line.Property(l => l.TaxCode).HasMaxLength(10).IsRequired();
            line.Property(l => l.Quantity).HasPrecision(18, 4);
            line.Property(l => l.UnitPrice).HasPrecision(18, 4);
            line.Property(l => l.TaxPercentage).HasPrecision(5, 2);
            line.Property(l => l.NetAmount).HasPrecision(18, 2);
            line.Property(l => l.TaxAmount).HasPrecision(18, 2);

            line.HasIndex(l => new { l.CreditNoteId, l.LineNumber }).IsUnique();
        });

        builder.Entity<Receipt>(receipt =>
        {
            receipt.ToTable("receipt");
            receipt.HasKey(r => r.Id);
            receipt.Property(r => r.Version).IsConcurrencyToken();

            receipt.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            receipt.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            receipt.Property(r => r.Method).HasConversion<string>().HasMaxLength(5);
            receipt.Property(r => r.Notes).HasMaxLength(500);
            receipt.Property(r => r.CancellationReason).HasMaxLength(500);
            receipt.Property(r => r.FiscalNotice).HasMaxLength(300);
            receipt.Property(r => r.Total).HasPrecision(18, 2);

            receipt.OwnsOne(r => r.Number, number =>
            {
                number.Property(x => x.Type).HasColumnName("number_type").HasConversion<string>().HasMaxLength(5);
                number.Property(x => x.Series).HasColumnName("number_series").HasMaxLength(20).IsRequired();
                number.Property(x => x.Sequence).HasColumnName("number_sequence").IsRequired();
                number.Ignore(x => x.Formatted);
                number.HasIndex(x => new { x.Type, x.Series, x.Sequence }).IsUnique();
            });

            receipt.OwnsOne(r => r.Customer, party =>
            {
                party.Property(p => p.Name).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
                party.Property(p => p.TaxId).HasColumnName("customer_tax_id").HasMaxLength(30).IsRequired();
                party.Property(p => p.AddressDetail).HasColumnName("customer_address_detail").HasMaxLength(300).IsRequired();
                party.Property(p => p.City).HasColumnName("customer_city").HasMaxLength(100).IsRequired();
                party.Property(p => p.Country).HasColumnName("customer_country").HasMaxLength(2).IsRequired();
                party.Property(p => p.IsFinalConsumer).HasColumnName("customer_is_final_consumer");
            });

            receipt.HasIndex(r => r.CustomerId);
            receipt.HasIndex(r => r.ReceivedOn);

            receipt.HasMany(r => r.Lines)
                .WithOne()
                .HasForeignKey(l => l.ReceiptId)
                .OnDelete(DeleteBehavior.Cascade);

            receipt.Navigation(r => r.Lines)
                .HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<ReceiptLine>(line =>
        {
            line.ToTable("receipt_line");
            line.HasKey(l => l.Id);

            line.Property(l => l.InvoiceNumber).HasMaxLength(40).IsRequired();
            line.Property(l => l.Amount).HasPrecision(18, 2);

            // A consulta do saldo: quanto foi recebido por factura.
            line.HasIndex(l => l.SalesInvoiceId);
            line.HasIndex(l => new { l.ReceiptId, l.LineNumber }).IsUnique();
        });

        builder.Entity<BankAccount>(account =>
        {
            account.ToTable("bank_account");
            account.HasKey(a => a.Id);

            // **Aqui é a regra, não formalidade** (BR-17): é o que faz dois
            // pagamentos simultâneos sobre a mesma conta colidirem em vez de
            // passarem os dois com o mesmo saldo lido.
            account.Property(a => a.Version).IsConcurrencyToken();

            account.Property(a => a.Name).HasMaxLength(120).IsRequired();
            account.Property(a => a.Bank).HasMaxLength(120).IsRequired();
            account.Property(a => a.Iban).HasMaxLength(40);
            account.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            account.Property(a => a.Balance).HasPrecision(18, 2);

            account.HasIndex(a => a.Iban).IsUnique().HasFilter("[iban] IS NOT NULL");
        });

        builder.Entity<BankMovement>(movement =>
        {
            movement.ToTable("bank_movement");
            movement.HasKey(m => m.Id);

            // **Sem contador de concorrência, e é deliberado.** Um movimento
            // nunca é alterado — quem colide é o saldo da conta, e é lá que
            // BR-17 age.
            movement.Property(m => m.Direction).HasConversion<string>().HasMaxLength(10);
            movement.Property(m => m.Amount).HasPrecision(18, 2);
            movement.Property(m => m.BalanceAfter).HasPrecision(18, 2);
            movement.Property(m => m.Description).HasMaxLength(300).IsRequired();
            movement.Property(m => m.SourceType).HasMaxLength(40);

            movement.HasOne<BankAccount>()
                .WithMany(a => a.Movements)
                .HasForeignKey(m => m.BankAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // O índice do extracto: uma conta, ordenada no tempo.
            movement.HasIndex(m => new { m.BankAccountId, m.OccurredAt });

            // Do movimento de volta ao documento que o causou — o percurso da
            // reconciliação.
            movement.HasIndex(m => new { m.SourceType, m.SourceId })
                .HasFilter("[source_id] IS NOT NULL");

            // Campo de suporte, configurado depois da relação existir: o
            // caminho de escrita nunca carrega o extracto, e acrescentar um
            // movimento não obriga a ler os anteriores.
            builder.Entity<BankAccount>()
                .Metadata
                .FindNavigation(nameof(BankAccount.Movements))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<PurchaseInvoice>(invoice =>
        {
            invoice.ToTable("purchase_invoice");
            invoice.HasKey(i => i.Id);
            invoice.Property(i => i.Version).IsConcurrencyToken();

            invoice.Property(i => i.SupplierInvoiceNumber).HasMaxLength(60).IsRequired();
            invoice.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            invoice.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            invoice.Property(i => i.Description).HasMaxLength(500);
            invoice.Property(i => i.CancellationReason).HasMaxLength(500);

            invoice.Property(i => i.NetTotal).HasPrecision(18, 2);
            invoice.Property(i => i.TaxTotal).HasPrecision(18, 2);
            invoice.Property(i => i.GrossTotal).HasPrecision(18, 2);

            invoice.Property(i => i.SupplierName).HasMaxLength(200).IsRequired();
            invoice.Property(i => i.SupplierTaxId).HasMaxLength(30).IsRequired();

            // `Supplier` compõe as duas colunas acima — é retrato, não coluna.
            invoice.Ignore(i => i.Supplier);

            // **Único**, e é a segunda linha contra pagar a dobrar: registar a
            // mesma factura do mesmo fornecedor duas vezes. A verificação em
            // `RegisterPurchaseInvoice` é a primeira; esta é a que resiste a
            // duas chamadas simultâneas.
            invoice.HasIndex(i => new { i.SupplierTaxId, i.SupplierInvoiceNumber })
                .IsUnique()
                .HasDatabaseName("ux_purchase_invoice_supplier_number");

            // A fila de pagamentos ordena-se por vencimento.
            invoice.HasIndex(i => i.DueOn);
        });

        builder.Entity<PaymentRequest>(request =>
        {
            request.ToTable("payment_request");
            request.HasKey(r => r.Id);
            request.Property(r => r.Version).IsConcurrencyToken();

            request.Property(r => r.SupplierInvoiceNumber).HasMaxLength(60).IsRequired();
            request.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            request.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            request.Property(r => r.Amount).HasPrecision(18, 2);
            request.Property(r => r.Notes).HasMaxLength(500);
            request.Property(r => r.CancellationReason).HasMaxLength(500);
            request.Property(r => r.ExecutedMethod).HasConversion<string>().HasMaxLength(5);
            request.Property(r => r.ExecutionReference).HasMaxLength(100);

            request.OwnsOne(r => r.Payee, payee =>
            {
                payee.Property(p => p.Name).HasColumnName("payee_name").HasMaxLength(200).IsRequired();
                payee.Property(p => p.TaxId).HasColumnName("payee_tax_id").HasMaxLength(30).IsRequired();
            });

            // Sem chave estrangeira para `approval.request`: são schemas de
            // módulos distintos, e `finance` referencia o processo por
            // identificador (ADR-010). O estado dele **não** é copiado para cá.
            request.HasIndex(r => r.ApprovalRequestId);

            // Quanto está comprometido sobre uma factura.
            request.HasIndex(r => new { r.PurchaseInvoiceId, r.Status });

            // Quem pagou o quê — a consulta de quem confere BR-3 depois.
            request.HasIndex(r => r.ExecutedByEmployeeId);
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
