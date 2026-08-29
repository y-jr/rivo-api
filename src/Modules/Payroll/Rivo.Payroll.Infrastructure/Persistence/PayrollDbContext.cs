using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Payroll.Domain;

namespace Rivo.Payroll.Infrastructure.Persistence;

public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options) : DbContext(options)
{
    public const string Schema = "payroll";

    public DbSet<PayrollRun> Runs => Set<PayrollRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<PayrollRun>(run =>
        {
            run.ToTable("payroll_run");
            run.HasKey(r => r.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            run.Property(r => r.Version).IsConcurrencyToken();

            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            run.HasIndex(r => new { r.Year, r.Month });

            // Itens como colecção própria — não há tabela separada exposta
            // fora do agregado, mas em SQL Server é sempre uma tabela: o EF
            // Core não tem colunas de tabela aninhada.
            run.HasMany(r => r.Items)
                .WithOne()
                .HasForeignKey(i => i.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            run.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<PayrollItem>(item =>
        {
            item.ToTable("payroll_item");
            item.HasKey(i => i.Id);

            item.Property(i => i.Version).IsConcurrencyToken();

            item.Property(i => i.GrossSalary).HasPrecision(18, 2);
            item.Property(i => i.NetSalary).HasPrecision(18, 2);
            item.Property(i => i.WithholdingTax).HasPrecision(18, 2);
            item.Property(i => i.SocialSecurityContribution).HasPrecision(18, 2);
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
    /// O domínio nunca lhe toca — ver a nota em <c>PayrollRun.Version</c>.
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
