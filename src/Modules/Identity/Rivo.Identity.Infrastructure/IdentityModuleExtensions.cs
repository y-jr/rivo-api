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
using Rivo.Identity.Contracts;
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
            .UseSqlServer(connectionString, sqlServer =>
                sqlServer.MigrationsHistoryTable("__ef_migrations_history", RivoIdentityDbContext.Schema)
                    // Resiliencia de ligacao: o SQL Server e externo ao container e
                    // vive noutra maquina. Falhas de rede transitorias sao normais,
                    // nao excepcionais — e o arranque pode apanhar a base indisponivel.
                    .EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            // Tabelas, colunas, índices e chaves em snake_case (standards/naming.md).
            //
            // A convenção nasceu do PostgreSQL, que dobra para minúsculas tudo o
            // que não venha citado. Em SQL Server deixou de ser obrigatória — e
            // mantém-se na mesma, porque é o padrão de nomes escrito do projecto
            // e trocá-la renomearia o esquema inteiro sem que nenhum requisito o
            // peça (ADR-029).
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
            .AddEntityFrameworkStores<RivoIdentityDbContext>()

            // Necessário para `GeneratePasswordResetTokenAsync`, que é a via
            // suportada de repor uma password sem conhecer a actual. Sem isto,
            // repor rebentava com "No IUserTwoFactorTokenProvider named
            // Default is registered" — e a alternativa era mexer no hash à mão,
            // que saltaria as regras de password e o carimbo de segurança.
            //
            // O token é gerado e consumido no mesmo pedido, por isso a
            // persistência do anel de chaves não é aqui um problema.
            .AddDefaultTokenProviders();

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

        // Login federado (ADR-032). Sem `ValidateOnStart` de propósito: o
        // Google é opcional, e exigi-lo derrubaria o arranque em
        // desenvolvimento e no CI, que não têm credenciais da Google.
        services
            .AddOptions<GoogleAuthOptions>()
            .Bind(configuration.GetSection(GoogleAuthOptions.SectionName));

        // Singleton porque o verificador cacheia o documento de descoberta e as
        // chaves da Google. Scoped faria um pedido HTTP à Google por login.
        services.AddSingleton<IExternalIdentityVerifier, GoogleIdentityVerifier>();

        services.AddScoped<SessionIssuer>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LogIn>();
        services.AddScoped<LogInWithGoogle>();
        services.AddScoped<LogOut>();
        services.AddScoped<ListUsers>();
        services.AddScoped<AssignAccessProfile>();
        services.AddScoped<RemoveAccessProfile>();
        services.AddScoped<ChangeOwnPassword>();
        services.AddScoped<ResetUserPassword>();
        services.AddScoped<SetAccountStatus>();
        services.AddScoped<ListOwnSessions>();
        services.AddScoped<RevokeOwnSession>();
        services.AddSingleton<ListAccessProfiles>();

        // O contrato publicado (ADR-017) — primeiro consumidor é `Rivo.Settings`
        // (ADR-041). Singleton pela mesma razão de `ListAccessProfiles`: lê um
        // catálogo estático em código, sem estado nem ligação a nada.
        services.AddSingleton<IAccessProfileCatalogue, AccessProfileCatalogue>();

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
            foreach (var permission in IdentityPermissions.All)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(IdentityPermissions.ClaimType, permission));
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
    public static async Task MigrateIdentityModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<RivoIdentityDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Semeia os Perfis de Acesso e os utilizadores de arranque.
    ///
    /// <para>
    /// <strong>Separado da migração de propósito</strong> (ADR-028). O ADR-020
    /// tirou a migração do arranque por duas razões — várias instâncias a
    /// competir pelo mesmo schema, e uma migração destrutiva a correr sem
    /// ninguém aprovar. <strong>O seed não tem nenhuma delas:</strong> é
    /// idempotente por desenho (ADR-016) e só acrescenta.
    /// </para>
    ///
    /// <para>
    /// Ficaram juntos por acidente de implementação, e o preço apareceu no
    /// primeiro deployment: staging tinha as tabelas todas e nenhum Perfil de
    /// Acesso, porque o gate de `Development` levou o seed atrás da migração.
    /// </para>
    ///
    /// <para>
    /// Pressupõe que as migrações já correram — quem chama garante a ordem.
    /// </para>
    /// </summary>
    public static async Task SeedIdentityModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<AccessProfileSeeder>()
            .SeedAsync(cancellationToken);

        await scope.ServiceProvider
            .GetRequiredService<BootstrapUserSeeder>()
            .SeedAsync(cancellationToken);
    }
}



