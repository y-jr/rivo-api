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
}
