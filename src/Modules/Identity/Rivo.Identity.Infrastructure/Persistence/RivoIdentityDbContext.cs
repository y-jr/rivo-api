using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rivo.Identity.Domain.Sessions;

namespace Rivo.Identity.Infrastructure.Persistence;

public sealed class RivoIdentityDbContext(DbContextOptions<RivoIdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Um schema por domínio, ownership exclusivo (ADR-002).
        builder.HasDefaultSchema(Schema);

        // Nomes em snake_case: o PostgreSQL dobra identificadores não citados para
        // minúsculas, e os nomes PascalCase do Identity ficariam permanentemente
        // dependentes de aspas.
        builder.Entity<ApplicationUser>().ToTable("app_user");
        builder.Entity<ApplicationRole>().ToTable("app_role");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("app_user_role");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("app_user_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("app_user_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("app_user_token");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("app_role_claim");

        builder.Entity<Session>(session =>
        {
            session.ToTable("user_session");
            session.HasKey(entity => entity.Id);

            // Endereço IPv6 em notação completa cabe em 45 caracteres.
            session.Property(entity => entity.IpAddress).HasMaxLength(45).IsRequired();
            session.Property(entity => entity.UserAgent).HasMaxLength(512);

            // Índice para a validação de sessão que corre a cada pedido
            // autenticado: procura por utilizador e filtra as ainda activas.
            session.HasIndex(entity => new { entity.UserId, entity.ExpiresAt });

            // Sem FK para app_user por opção: a sessão é um facto histórico e
            // deve sobreviver à remoção lógica da conta, tal como a auditoria.
        });
    }
}
