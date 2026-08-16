using Microsoft.EntityFrameworkCore;
using Rivo.Notifications.Domain;

namespace Rivo.Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Notification>(notification =>
        {
            notification.ToTable("notification");
            notification.HasKey(n => n.Id);

            // Concorrência optimista (ADR-002, ADR-025). O `UPDATE` passa a
            // filtrar por `version`, e se outra transacção tiver gravado
            // entretanto, afecta zero linhas e o EF Core lança
            // DbUpdateConcurrencyException em vez de sobrepor em silêncio.
            notification.Property(n => n.Version).IsConcurrencyToken();

            notification.Property(n => n.Type).HasMaxLength(100).IsRequired();
            notification.Property(n => n.Title).HasMaxLength(200).IsRequired();
            notification.Property(n => n.Message).HasMaxLength(2000);
            notification.Property(n => n.LastDeliveryError).HasMaxLength(500);
            notification.Property(n => n.DeliveryStatus).HasConversion<string>().HasMaxLength(20);

            // Consulta do destinatário: as suas, mais recentes primeiro.
            notification.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });

            // Consulta do worker: o que está em atraso. Índice parcial, porque
            // a esmagadora maioria das linhas nunca fica pendente e não vale a
            // pena indexá-las.
            notification.HasIndex(n => n.NextAttemptAt)
                .HasFilter("delivery_status = 'Pending'");

            // Sem chave estrangeira para identity.app_user: `notifications`
            // guarda o identificador do destinatário mas não conhece
            // `identity`, o que evita dependência de compilação e ciclo.
        });
    }

    /// <summary>
    /// Incrementa o contador de concorrência de tudo o que vai ser alterado.
    ///
    /// <para>
    /// O domínio nunca mexe no <c>Version</c>: obrigá-lo a lembrar-se disso em
    /// cada método que altera estado seria uma regra que se esquece uma vez e
    /// falha em silêncio para sempre. Aqui é impossível esquecer.
    /// </para>
    ///
    /// <para>
    /// Subir o <c>CurrentValue</c> é o suficiente — o EF Core usa o
    /// <c>OriginalValue</c> na cláusula <c>WHERE</c>, que é exactamente o que
    /// detecta a escrita concorrente.
    /// </para>
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var version = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(Notification.Version));

            if (version?.CurrentValue is int current)
            {
                version.CurrentValue = current + 1;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
