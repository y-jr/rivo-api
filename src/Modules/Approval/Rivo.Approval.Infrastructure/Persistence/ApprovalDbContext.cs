using Microsoft.EntityFrameworkCore;
using Rivo.Approval.Domain;

namespace Rivo.Approval.Infrastructure.Persistence;

public sealed class ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : DbContext(options)
{
    public const string Schema = "approval";

    public DbSet<ApprovalPolicy> Policies => Set<ApprovalPolicy>();

    public DbSet<ApprovalRequest> Requests => Set<ApprovalRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApprovalPolicy>(policy =>
        {
            policy.ToTable("policy");
            policy.HasKey(p => p.Id);
            // Concorrência optimista (ADR-002, ADR-025).
            policy.Property(p => p.Version).IsConcurrencyToken();
            policy.Property(p => p.ProcessType).HasMaxLength(100).IsRequired();
            policy.Property(p => p.MinimumAmount).HasPrecision(18, 2);
            policy.Property(p => p.MaximumAmount).HasPrecision(18, 2);

            // A consulta da submissão: políticas activas de um tipo.
            policy.HasIndex(p => new { p.ProcessType, p.IsActive });

            // Os passos são parte do agregado — acedem-se pela política e
            // morrem com ela.
            policy.HasMany(p => p.Steps)
                .WithOne()
                .HasForeignKey(s => s.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            policy.Navigation(p => p.Steps)
                .HasField("_steps")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<PolicyStep>(step =>
        {
            step.ToTable("policy_step");
            step.HasKey(s => s.Id);
            step.Property(s => s.Mode).HasConversion<string>().HasMaxLength(20);
            step.HasIndex(s => new { s.PolicyId, s.Order }).IsUnique();

            // Sem FK para hr.position: são schemas de módulos distintos, e
            // `approval` referencia Cargos por identificador (ADR-010).
            step.HasIndex(s => s.ApproverPositionId);
        });

        builder.Entity<ApprovalRequest>(request =>
        {
            request.ToTable("request");
            request.HasKey(r => r.Id);

            // BR-17: duas decisões simultâneas sobre o mesmo pedido — uma perde.
            request.Property(r => r.Version).IsConcurrencyToken();

            request.Property(r => r.ProcessType).HasMaxLength(100).IsRequired();
            request.Property(r => r.SourceModule).HasMaxLength(50).IsRequired();
            request.Property(r => r.SourceReference).HasMaxLength(200).IsRequired();
            request.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
            request.Property(r => r.Amount).HasPrecision(18, 2);
            request.Property(r => r.Currency).HasMaxLength(3);
            request.Property(r => r.Summary).HasMaxLength(1000);

            // Sem FK para `policy`, e é deliberado (BR-6, ADR-034): a política
            // aplicada é rasto histórico. Uma FK viva convidaria a segui-la
            // para recalcular o processo — que é exactamente o que não pode
            // acontecer.
            request.Property(r => r.AppliedPolicyId).IsRequired();

            // A consulta do módulo de origem: "o que aconteceu ao meu pedido".
            request.HasIndex(r => new { r.SourceModule, r.SourceReference });
            request.HasIndex(r => new { r.ProcessType, r.Status });

            request.HasMany(r => r.Assignments)
                .WithOne()
                .HasForeignKey(a => a.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            request.Navigation(r => r.Assignments)
                .HasField("_assignments")
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            request.HasMany(r => r.Decisions)
                .WithOne()
                .HasForeignKey(d => d.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            request.Navigation(r => r.Decisions)
                .HasField("_decisions")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<Assignment>(assignment =>
        {
            assignment.ToTable("assignment");
            assignment.HasKey(a => a.Id);
            assignment.Property(a => a.Mode).HasConversion<string>().HasMaxLength(20);

            // A caixa de entrada de quem aprova: pedidos à espera desta pessoa.
            assignment.HasIndex(a => new { a.ApproverEmployeeId, a.HasDecided });
            assignment.HasIndex(a => new { a.RequestId, a.Step });
        });

        builder.Entity<Decision>(decision =>
        {
            decision.ToTable("decision");
            decision.HasKey(d => d.Id);
            decision.Property(d => d.Action).HasConversion<string>().HasMaxLength(30);
            decision.Property(d => d.Notes).HasMaxLength(2000);
            decision.HasIndex(d => new { d.RequestId, d.Step });

            // Quem interveio num processo — a consulta de BR-4.
            decision.HasIndex(d => d.DecidedByEmployeeId);
        });
    }

    /// <summary>
    /// Incrementa o contador de concorrência de tudo o que vai ser alterado.
    ///
    /// <para>
    /// O domínio nunca mexe no <c>Version</c>: obrigá-lo a lembrar-se disso em
    /// cada método que altera estado seria uma regra que se esquece uma vez e
    /// falha em silêncio para sempre.
    /// </para>
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
