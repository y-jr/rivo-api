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

    public DbSet<ChartOfAccountsVersion> ChartOfAccountsVersions => Set<ChartOfAccountsVersion>();

    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();

    public DbSet<Journal> Journals => Set<Journal>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();

    public DbSet<PostingRule> PostingRules => Set<PostingRule>();

    public DbSet<AccountingRule> AccountingRules => Set<AccountingRule>();

    public DbSet<CostCentre> CostCentres => Set<CostCentre>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<DepartmentCostForecast> CostForecasts => Set<DepartmentCostForecast>();

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

            // O consumo orçamental de BR-8: quanto está comprometido num centro
            // de custo num mês.
            request.HasIndex(r => new { r.CostCentreId, r.RequestedOn, r.Status })
                .HasFilter("[cost_centre_id] IS NOT NULL");
        });

        // ---------- Contabilidade ----------

        builder.Entity<ChartOfAccountsVersion>(version =>
        {
            version.ToTable("chart_of_accounts_version");
            version.HasKey(v => v.Id);
            version.Property(v => v.Version).IsConcurrencyToken();
            version.Property(v => v.Revision).HasMaxLength(30).IsRequired().HasColumnName("revision");
            version.Property(v => v.Jurisdiction).HasMaxLength(30).IsRequired();
            version.Property(v => v.Name).HasMaxLength(60).IsRequired();
            version.Property(v => v.Source).HasMaxLength(300).IsRequired();
            version.Property(v => v.IsActive);

            version.HasIndex(v => new { v.Jurisdiction, v.Name, v.Revision }).IsUnique();

            version.Navigation(v => v.Accounts)
                .HasField("_accounts")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<LedgerAccount>(account =>
        {
            account.ToTable("ledger_account");
            account.HasKey(a => a.Id);
            account.Property(a => a.Version).IsConcurrencyToken();

            // 30 caracteres é o que o `SAFAOGLAccountID` admite.
            account.Property(a => a.Code).HasMaxLength(30).IsRequired();
            account.Property(a => a.Name).HasMaxLength(200).IsRequired();
            account.Property(a => a.ParentCode).HasMaxLength(30);
            account.Property(a => a.Category).HasConversion<string>().HasMaxLength(2);
            account.Property(a => a.ChartOfAccountsVersionId).IsRequired();

            // `AcceptsPostings`, `IsFirstDegree` e `IsAnalytic` são leituras da
            // categoria, não colunas.
            account.Ignore(a => a.AcceptsPostings);
            account.Ignore(a => a.IsFirstDegree);
            account.Ignore(a => a.IsAnalytic);

            // Duas contas com o mesmo código tornariam o `GroupingCode` ambíguo
            // e o ficheiro SAF-T inválido. É a segunda linha de defesa — a
            // primeira é a verificação no caso de uso.
            account.HasIndex(a => a.Code).IsUnique();

            account.HasOne<ChartOfAccountsVersion>()
                .WithMany(v => v.Accounts)
                .HasForeignKey(a => a.ChartOfAccountsVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            account.HasOne<LedgerAccount>()
                .WithMany()
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Journal>(journal =>
        {
            journal.ToTable("journal");
            journal.HasKey(j => j.Id);
            journal.Property(j => j.Version).IsConcurrencyToken();

            journal.Property(j => j.Code).HasMaxLength(30).IsRequired();
            journal.Property(j => j.Name).HasMaxLength(200).IsRequired();

            // `JournalID` é único no ficheiro SAF-T.
            journal.HasIndex(j => j.Code).IsUnique();
        });

        builder.Entity<JournalEntry>(entry =>
        {
            entry.ToTable("journal_entry");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Version).IsConcurrencyToken();

            entry.Property(e => e.JournalCode).HasMaxLength(30).IsRequired();
            entry.Property(e => e.ArchivalNumber).HasMaxLength(20).IsRequired();
            entry.Property(e => e.Description).HasMaxLength(300).IsRequired();
            entry.Property(e => e.SourceId).HasMaxLength(30).IsRequired();
            entry.Property(e => e.Type).HasConversion<string>().HasMaxLength(1);
            entry.Property(e => e.VoidReason).HasMaxLength(500);

            entry.Property(e => e.TotalDebit).HasPrecision(18, 2);
            entry.Property(e => e.TotalCredit).HasPrecision(18, 2);

            // `TransactionId` compõe data, diário e número de arquivo — é
            // derivado, não coluna.
            entry.Ignore(e => e.TransactionId);

            entry.HasOne<Journal>()
                .WithMany()
                .HasForeignKey(e => e.JournalId)
                .OnDelete(DeleteBehavior.Restrict);

            // **O `TransactionID` do SAF-T, único.** É composto por três coisas
            // que quem lança escolhe, e nada impede repeti-las por engano — o
            // ficheiro só seria recusado meses depois.
            entry.HasIndex(e => new { e.TransactionDate, e.JournalCode, e.ArchivalNumber })
                .IsUnique();

            // O balancete: lançamentos de um período.
            entry.HasIndex(e => new { e.TransactionDate, e.Period });

            entry.OwnsMany(e => e.Lines, line =>
            {
                line.ToTable("journal_entry_line");
                line.WithOwner().HasForeignKey(l => l.JournalEntryId);
                line.HasKey(l => l.Id);

                line.Property(l => l.AccountCode).HasMaxLength(30).IsRequired();
                line.Property(l => l.Description).HasMaxLength(300).IsRequired();
                line.Property(l => l.SourceDocumentId).HasMaxLength(60);
                line.Property(l => l.Side).HasConversion<string>().HasMaxLength(6);
                line.Property(l => l.Amount).HasPrecision(18, 2);

                // O balancete soma por conta.
                line.HasIndex(l => l.AccountId);

                // A contabilidade analítica: o que se gastou por centro de custo.
                line.HasIndex(l => l.CostCentreId).HasFilter("[cost_centre_id] IS NOT NULL");

                line.HasIndex(l => new { l.JournalEntryId, l.RecordNumber }).IsUnique();
            });
        });

        builder.Entity<AccountingPeriod>(period =>
        {
            period.ToTable("accounting_period");
            period.HasKey(p => p.Id);

            // **Aqui é a regra, não formalidade** (BR-17): um fecho simultâneo
            // a um lançamento é exactamente a corrida que deixaria um movimento
            // cair dentro de um período já dado por fechado.
            period.Property(p => p.Version).IsConcurrencyToken();

            period.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
            period.Property(p => p.ReopenReason).HasMaxLength(500);

            period.Ignore(p => p.AcceptsPostings);
            period.Ignore(p => p.IsAdjustmentPeriod);

            period.HasIndex(p => new { p.FiscalYear, p.Number }).IsUnique();
        });

        builder.Entity<PostingRule>(rule =>
        {
            rule.ToTable("posting_rule");
            rule.HasKey(r => r.Id);
            rule.Property(r => r.Version).IsConcurrencyToken();

            rule.Property(r => r.Event).HasConversion<string>().HasMaxLength(40);
            rule.Property(r => r.JournalCode).HasMaxLength(30).IsRequired();
            rule.Property(r => r.Description).HasMaxLength(200).IsRequired();

            // **Uma regra activa por acontecimento.** Duas tornariam a tradução
            // documento → contas ambígua, e o sistema não escolhe por si — o
            // mesmo princípio das políticas de aprovação empatadas (ADR-034).
            rule.HasIndex(r => r.Event).IsUnique().HasFilter("[is_active] = 1");

            rule.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("posting_rule_line");
                line.WithOwner().HasForeignKey(l => l.PostingRuleId);
                line.HasKey(l => l.Id);

                line.Property(l => l.AccountCode).HasMaxLength(30).IsRequired();
                line.Property(l => l.Description).HasMaxLength(200).IsRequired();
                line.Property(l => l.Side).HasConversion<string>().HasMaxLength(6);
                line.Property(l => l.Amount).HasConversion<string>().HasMaxLength(5);

                line.HasIndex(l => new { l.PostingRuleId, l.LineNumber }).IsUnique();
            });
        });

        builder.Entity<AccountingRule>(rule =>
        {
            rule.ToTable("accounting_rule");
            rule.HasKey(r => r.Id);
            rule.Property(r => r.Version).IsConcurrencyToken();

            rule.Property(r => r.Code).HasMaxLength(30).IsRequired();
            rule.Property(r => r.Name).HasMaxLength(200).IsRequired();
            rule.Property(r => r.SourceType).HasMaxLength(40).IsRequired();
            rule.Property(r => r.Source).HasMaxLength(200).IsRequired();
            rule.Property(r => r.IsActive);
            rule.Property(r => r.EffectiveFrom);
            rule.Property(r => r.EffectiveTo);

            rule.HasIndex(r => new { r.Code, r.EffectiveFrom }).IsUnique();

            // As linhas são um value object embutido, serializadas em JSON.
            rule.Property(r => r.Lines)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<AccountingRuleLine>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
                .HasColumnName("lines");
        });

        // ---------- Planeamento ----------

        builder.Entity<CostCentre>(centre =>
        {
            centre.ToTable("cost_centre");
            centre.HasKey(c => c.Id);
            centre.Property(c => c.Version).IsConcurrencyToken();

            centre.Property(c => c.Code).HasMaxLength(20).IsRequired();
            centre.Property(c => c.Name).HasMaxLength(200).IsRequired();

            centre.HasIndex(c => c.Code).IsUnique();

            // Sem chave estrangeira para `hr.department`: são schemas de
            // módulos distintos, e o mapeamento é opcional por desenho (D4).
            centre.HasIndex(c => c.DepartmentId).HasFilter("[department_id] IS NOT NULL");
        });

        builder.Entity<Budget>(budget =>
        {
            budget.ToTable("budget");
            budget.HasKey(b => b.Id);
            budget.Property(b => b.Version).IsConcurrencyToken();

            budget.Property(b => b.Currency).HasMaxLength(3).IsRequired();
            budget.Property(b => b.Status).HasConversion<string>().HasMaxLength(10);
            budget.Property(b => b.AnnualTotal).HasPrecision(18, 2);

            budget.Ignore(b => b.IsInForce);

            budget.HasOne<CostCentre>()
                .WithMany()
                .HasForeignKey(b => b.CostCentreId)
                .OnDelete(DeleteBehavior.Restrict);

            // **Um orçamento por centro de custo e ano.** Dois tectos para o
            // mesmo ano tornariam a verificação de BR-8 ambígua — e uma
            // verificação ambígua não verifica nada.
            budget.HasIndex(b => new { b.CostCentreId, b.FiscalYear }).IsUnique();

            budget.OwnsMany(b => b.Lines, line =>
            {
                line.ToTable("budget_line");
                line.WithOwner().HasForeignKey(l => l.BudgetId);
                line.HasKey(l => l.Id);

                line.Property(l => l.Amount).HasPrecision(18, 2);

                line.HasIndex(l => new { l.BudgetId, l.Month }).IsUnique();
            });
        });

        builder.Entity<DepartmentCostForecast>(forecast =>
        {
            forecast.ToTable("cost_forecast");
            forecast.HasKey(f => f.Id);
            forecast.Property(f => f.Version).IsConcurrencyToken();

            forecast.Property(f => f.Currency).HasMaxLength(3).IsRequired();
            forecast.Property(f => f.Status).HasConversion<string>().HasMaxLength(10);
            forecast.Property(f => f.OperationalCosts).HasPrecision(18, 2);
            forecast.Property(f => f.FixedCosts).HasPrecision(18, 2);

            // `Total` é a soma das duas — derivado, não coluna.
            forecast.Ignore(f => f.Total);

            // Uma previsão por departamento e mês. Duas seriam dois números a
            // dizer coisas diferentes sobre o mesmo carregamento de caixa.
            forecast.HasIndex(f => new { f.DepartmentId, f.FiscalYear, f.Month }).IsUnique();
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
