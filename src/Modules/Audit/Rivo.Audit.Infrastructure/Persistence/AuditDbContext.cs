using Microsoft.EntityFrameworkCore;
using Rivo.Audit.Domain;

namespace Rivo.Audit.Infrastructure.Persistence;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Schema próprio, ownership exclusivo (ADR-002). `audit` não partilha
        // tabelas com nenhum módulo e ninguém escreve nas suas.
        builder.HasDefaultSchema(Schema);

        builder.Entity<AuditEvent>(entry =>
        {
            entry.ToTable("audit_event");
            entry.HasKey(e => e.Id);

            entry.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entry.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entry.Property(e => e.EntityId).HasMaxLength(100).IsRequired();
            entry.Property(e => e.IpAddress).HasMaxLength(45);
            entry.Property(e => e.CorrelationId).HasMaxLength(100);

            // jsonb em vez de texto: permite consultar dentro do valor sem
            // desserializar, quando a investigação o exigir.
            entry.Property(e => e.PreviousValue).HasColumnType("jsonb");
            entry.Property(e => e.NewValue).HasColumnType("jsonb");

            // Consulta típica: trilha de um registo, mais recente primeiro.
            entry.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt });

            // Segunda consulta típica: o que fez determinado actor.
            entry.HasIndex(e => new { e.ActorId, e.OccurredAt });

            // Sem chave estrangeira para identity.app_user, por desenho: a
            // trilha tem de sobreviver à eliminação lógica da conta que
            // descreve (ADR-009).
        });
    }
}
