using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rivo.Messaging.Domain;

namespace Rivo.Messaging.Infrastructure.Persistence;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public const string Schema = "messaging";

    public DbSet<Conversation> Conversations => Set<Conversation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversation");
            conversation.HasKey(c => c.Id);
            conversation.Property(c => c.Version).IsConcurrencyToken();

            conversation.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
            conversation.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
            conversation.Property(c => c.Subject).HasMaxLength(200);

            // Sem chave estrangeira para `commercial.customer`: schemas de
            // módulos distintos, referência por identificador (ADR-010).
            //
            // A invariante "uma aberta por cliente" — só para mensagens
            // directas (ADR-046 §4; tickets podem ter várias abertas ao
            // mesmo tempo, cada uma com o seu assunto). Um índice filtrado e
            // único é a segunda linha de defesa — a primeira é
            // `FindOpenByCustomerAsync` na camada Application, que não
            // basta sozinha contra duas chamadas simultâneas. Serve também
            // a consulta "todas as conversas de um cliente" — o volume por
            // cliente é baixo, e um segundo índice sem filtro só para essa
            // leitura seria peso morto (mesma nota do ADR-045 original).
            conversation.HasIndex(c => c.CustomerId)
                .IsUnique()
                .HasDatabaseName("ux_conversation_open_message_per_customer")
                .HasFilter("[status] = 'Open' AND [kind] = 'Message'");

            conversation.HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            conversation.Navigation(c => c.Messages)
                .HasField("_messages")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<Message>(message =>
        {
            message.ToTable("message");
            message.HasKey(m => m.Id);

            // Sem contador de concorrência, e é deliberado: uma mensagem
            // nunca é alterada depois de escrita. Quem colide é a conversa
            // (fechá-la ao mesmo tempo que se lhe responde), e é lá que o
            // token de concorrência age.
            message.Property(m => m.Sender).HasConversion<string>().HasMaxLength(10);
            message.Property(m => m.Body).HasMaxLength(4000).IsRequired();

            message.HasIndex(m => new { m.ConversationId, m.SentAt });
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
