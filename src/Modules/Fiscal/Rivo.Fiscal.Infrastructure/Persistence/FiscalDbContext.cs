using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Fiscal.Domain;

namespace Rivo.Fiscal.Infrastructure.Persistence;

public sealed class FiscalDbContext(DbContextOptions<FiscalDbContext> options) : DbContext(options)
{
    public const string Schema = "fiscal";

    public DbSet<TaxRateSchedule> Schedules => Set<TaxRateSchedule>();

    public DbSet<IncomeTaxSchedule> IncomeTaxSchedules => Set<IncomeTaxSchedule>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<TaxRateSchedule>(schedule =>
        {
            schedule.ToTable("tax_rate_schedule");
            schedule.HasKey(s => s.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            schedule.Property(s => s.Version).IsConcurrencyToken();

            schedule.Property(s => s.Kind).HasConversion<string>().HasMaxLength(30);
            schedule.Property(s => s.Code).HasMaxLength(10).IsRequired();
            schedule.Property(s => s.Description).HasMaxLength(200).IsRequired();

            // Uma série por imposto e código. É a metade da invariante que o
            // agregado não consegue impor: ele garante que as *suas* versões
            // não se sobrepõem, mas não vê as outras séries.
            schedule.HasIndex(s => new { s.Kind, s.Code }).IsUnique();

            // `RequiresExemptionCode` é uma pergunta sobre `Code`, não uma
            // coluna. Sem isto o EF tentaria mapeá-la e falharia por não ter
            // setter.
            schedule.Ignore(s => s.RequiresExemptionCode);

            schedule.HasMany(s => s.Versions)
                .WithOne()
                .HasForeignKey("tax_rate_schedule_id")
                .OnDelete(DeleteBehavior.Cascade);

            schedule.Navigation(s => s.Versions)
                .HasField("_versions")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<TaxRateVersion>(version =>
        {
            version.ToTable("tax_rate_version");
            version.HasKey(v => v.Id);

            // Precisão fixada: uma taxa é `14.00`, não um binário aproximado.
            // Sem isto o SQL Server escolheria a omissão e 17,5% deixaria de
            // ser exactamente 17,5%.
            version.Property(v => v.Percentage).HasPrecision(5, 2);

            version.Property(v => v.LegalInstrument).HasMaxLength(200).IsRequired();

            // A consulta da determinação: que versão cobria esta data.
            version.HasIndex(v => v.EffectiveFrom);
        });

        builder.Entity<IncomeTaxSchedule>(schedule =>
        {
            schedule.ToTable("income_tax_schedule");
            schedule.HasKey(s => s.Id);

            schedule.Property(s => s.Version).IsConcurrencyToken();

            schedule.HasMany(s => s.Versions)
                .WithOne()
                .HasForeignKey("income_tax_schedule_id")
                .OnDelete(DeleteBehavior.Cascade);

            schedule.Navigation(s => s.Versions)
                .HasField("_versions")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<IncomeTaxScheduleVersion>(version =>
        {
            version.ToTable("income_tax_schedule_version");
            version.HasKey(v => v.Id);

            version.Property(v => v.LegalInstrument).HasMaxLength(200).IsRequired();

            // A consulta da determinação: que versão cobria esta data.
            version.HasIndex(v => v.EffectiveFrom);

            version.HasMany(v => v.Brackets)
                .WithOne()
                .HasForeignKey("income_tax_schedule_version_id")
                .OnDelete(DeleteBehavior.Cascade);

            version.Navigation(v => v.Brackets)
                .HasField("_brackets")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<IncomeTaxBracket>(bracket =>
        {
            bracket.ToTable("income_tax_bracket");
            bracket.HasKey(b => b.Id);

            // 18,2: mesma precisão que `finance` usa para valores monetários
            // — a parcela fixa e o "excesso de" são Kwanzas.
            bracket.Property(b => b.LowerBound).HasPrecision(18, 2);
            bracket.Property(b => b.FixedPortion).HasPrecision(18, 2);

            // A taxa é percentagem, mesma precisão de `TaxRateVersion.Percentage`.
            bracket.Property(b => b.Rate).HasPrecision(5, 2);
        });

        // As chaves são geradas pelo domínio (`Guid.CreateVersion7`), nunca
        // pela base de dados. Ver a nota longa em ApprovalDbContext: sem isto
        // uma versão acrescentada a uma série já rastreada sairia como UPDATE
        // de uma linha que nunca foi inserida.
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
    /// O domínio nunca lhe toca — ver a nota em <c>TaxRateSchedule.Version</c>.
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
