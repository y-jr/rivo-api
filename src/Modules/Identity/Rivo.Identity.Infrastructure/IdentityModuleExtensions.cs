using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Rivo.Identity.Application.Abstractions;
using Rivo.Identity.Application.Authorization;
using Rivo.Identity.Application.UseCases;
using Rivo.Identity.Infrastructure.Identity;
using Rivo.Identity.Infrastructure.Persistence;
using Rivo.Identity.Infrastructure.Sessions;
using Rivo.Identity.Infrastructure.Tokens;

namespace Rivo.Identity.Infrastructure;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Rivo")
            ?? throw new InvalidOperationException("Falta a connection string 'Rivo'.");

        services.AddDbContext<RivoIdentityDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", RivoIdentityDbContext.Schema)
                    // Resiliencia de ligacao: a base de dados pode nao estar
                    // pronta no arranque (o depends_on do compose so vale no up,
                    // nao no restart), e em producao ha failover e reinicios.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            // Tabelas, colunas, índices e chaves em snake_case (standards/naming.md).
            // Sem isto, os nomes PascalCase do .NET ficariam permanentemente
            // dependentes de aspas: o PostgreSQL dobra para minúsculas tudo o
            // que não venha citado.
            .UseSnakeCaseNamingConvention());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Política de password robusta, exigida pelos requisitos de
                // segurança absorvidos do SGAP.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.User.RequireUniqueEmail = true;

                // Bloqueio após tentativas falhadas, para travar força bruta.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<RivoIdentityDbContext>();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            // Falha no arranque, não no primeiro login.
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<ISessionStore, SessionStore>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LogIn>();
        services.AddScoped<LogOut>();
        services.AddScoped<ListUsers>();
        services.AddScoped<AssignAccessProfile>();
        services.AddSingleton<ListAccessProfiles>();

        services.AddScoped<AccessProfileSeeder>();

        services
            .AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName))
            .ValidateDataAnnotations()
            // Configuração inválida impede o arranque, em vez de deixar o
            // ambiente sem administrador sem ninguém dar por isso.
            .ValidateOnStart();

        services.AddScoped<BootstrapUserSeeder>();

        // Os parâmetros de validação do JWT são necessários no momento do
        // registo, antes de existir contentor. Lêem-se directamente da
        // configuração — nunca construir um IServiceProvider intermédio aqui.
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"Falta a secção de configuração '{JwtOptions.SectionName}'.");

        AddJwtAuthentication(services, jwt);

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, JwtOptions jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,

                    // Sem tolerância: um token expirado deixa de servir no
                    // instante em que expira. O valor por omissão são 5 minutos.
                    ClockSkew = TimeSpan.Zero,
                };

                options.Events = new JwtBearerEvents
                {
                    // Assinatura válida não basta: a sessão a que o token
                    // pertence tem de continuar activa. É isto que torna a
                    // revogação efectiva.
                    OnTokenValidated = async context =>
                    {
                        var sessionId = context.Principal?.FindFirstValue(ClaimTypes.Sid)
                            ?? context.Principal?.FindFirstValue("sid");

                        if (!Guid.TryParse(sessionId, out var id))
                        {
                            context.Fail("Token sem identificador de sessão.");
                            return;
                        }

                        var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
                        var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();

                        var session = await sessions.FindAsync(id, context.HttpContext.RequestAborted);

                        if (session is null || !session.IsActiveAt(clock.GetUtcNow()))
                        {
                            context.Fail("Sessão terminada ou expirada.");
                        }
                    },
                };
            });

        // Uma policy por permissão, registada a partir do catálogo. Assim o
        // endpoint declara `.RequireAuthorization("identity.roles.read")` e não
        // há verificações de autorização espalhadas pelos handlers.
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(Permissions.ClaimType, permission));
            }
        });
    }

    /// <summary>
    /// Aplica migrações e semeia perfis, permissões e utilizadores iniciais.
    ///
    /// <para>Chamado pelo host no arranque. A ordem é obrigatória:</para>
    /// <code>
    /// Migrations → Perfis + Permissões → Utilizadores + Associações
    /// </code>
    /// <para>
    /// Sem schema não há onde semear; sem perfis não há a que associar os
    /// utilizadores.
    /// </para>
    /// </summary>
    public static async Task InitialiseIdentityModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<RivoIdentityDbContext>()
            .Database.MigrateAsync(cancellationToken);

        await scope.ServiceProvider
            .GetRequiredService<AccessProfileSeeder>()
            .SeedAsync(cancellationToken);

        await scope.ServiceProvider
            .GetRequiredService<BootstrapUserSeeder>()
            .SeedAsync(cancellationToken);
    }
}



