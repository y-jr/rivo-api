using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Commercial.Domain;

namespace Rivo.Commercial.Infrastructure.Persistence;

public sealed class CommercialDbContext(DbContextOptions<CommercialDbContext> options) : DbContext(options)
{
    public const string Schema = "commercial";

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<Customer>(customer =>
        {
            customer.ToTable("customer");
            customer.HasKey(c => c.Id);

            // Concorrência optimista (ADR-002, ADR-025).
            customer.Property(c => c.Version).IsConcurrencyToken();

            customer.Property(c => c.Name).HasMaxLength(200).IsRequired();
            customer.Property(c => c.TaxId).HasMaxLength(30).IsRequired();
            customer.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            customer.Property(c => c.Email).HasMaxLength(200);
            customer.Property(c => c.Phone).HasMaxLength(50);

            // A segunda linha da unicidade do NIF. A primeira é a verificação
            // em RegisterCustomer, que não basta: duas chamadas simultâneas
            // passam as duas por lá, e é aqui que uma perde.
            customer.HasIndex(c => c.TaxId).IsUnique();

            customer.HasIndex(c => c.Name);

            // Único quando preenchido — o SQL Server trata cada NULL como
            // distinto, por isso vários clientes sem conta ligada continuam
            // a caber. Mesmo desenho de Employee.UserId (ADR-042).
            customer.HasIndex(c => c.UserId).IsUnique();

            // Sem chave estrangeira para `hr.employee`: schemas de módulos
            // distintos, referência por identificador (ADR-010). Não é
            // único — o mesmo vendedor pode responder por vários clientes.
            customer.HasIndex(c => c.AssignedToEmployeeId)
                .HasFilter("[assigned_to_employee_id] IS NOT NULL");

            // Morada como propriedade owned: colunas na mesma tabela, e não
            // uma linha à parte. É objecto de valor — não tem identidade nem
            // ciclo de vida próprio.
            customer.OwnsOne(c => c.BillingAddress, address =>
            {
                address.Property(a => a.Detail).HasColumnName("billing_detail").HasMaxLength(300).IsRequired();
                address.Property(a => a.City).HasColumnName("billing_city").HasMaxLength(100).IsRequired();
                address.Property(a => a.Country).HasColumnName("billing_country").HasMaxLength(2).IsRequired();
            });
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
    /// O domínio nunca lhe toca — ver a nota em <c>Customer.Version</c>.
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
