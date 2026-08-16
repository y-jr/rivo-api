using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Rivo.Api.OpenApi;
using Rivo.Audit.Api;
using Rivo.Audit.Infrastructure;
using Rivo.Documents.Api;
using Rivo.Documents.Infrastructure;
using Rivo.Hr.Api;
using Rivo.Hr.Infrastructure;
using Rivo.Identity.Api;
using Rivo.Notifications.Api;
using Rivo.Notifications.Infrastructure;
using Rivo.Identity.Infrastructure;
using Rivo.Identity.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

// Cada módulo regista os seus próprios serviços e policies. A ordem segue as
// dependências de contrato: `identity` consome os contratos de `audit` e `hr`.
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddHrModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);

var app = builder.Build();

// Documentação e interface só em desenvolvimento: expor a superfície da API
// em produção alarga o que um atacante sabe sem ter de adivinhar.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Rivo API"));

    // Migrações no arranque, para que `docker compose up` deixe o ambiente
    // utilizável num só comando.
    //
    // Deliberadamente restrito a Development: em produção, migrar
    // automaticamente no arranque é perigoso — várias instâncias competiriam
    // pelo mesmo schema, e uma migração destrutiva correria sem ninguém a
    // aprovar. Aí é passo próprio do pipeline (ADR-020).
    //
    // A ordem importa: `identity` vem por último porque o seu seed depende de
    // schemas dos outros.
    await app.Services.MigrateAuditModuleAsync();
    await app.Services.MigrateDocumentsModuleAsync();
    await app.Services.MigrateNotificationsModuleAsync();
    await app.Services.MigrateHrModuleAsync();
    await app.Services.MigrateIdentityModuleAsync();
}

// Seed fora de Production — ADR-028.
//
// **Não acompanha a migração, e a distinção é o ponto.** Migrar
// automaticamente é perigoso pelas razões acima; semear não é: o seed é
// idempotente (ADR-016), nunca altera contas existentes e só acrescenta o que
// falta.
//
// Estarem juntos era acidente de implementação, e o preço apareceu no primeiro
// deployment: staging ficou com as tabelas todas e nenhum Perfil de Acesso,
// porque o gate de `Development` levou o seed atrás da migração.
//
// Produção fica de fora: lá, criar o primeiro administrador é acto deliberado
// e auditado, não efeito colateral de um arranque.
if (!app.Environment.IsProduction())
{
    await app.Services.SeedIdentityModuleAsync();
}

// Cabeçalhos reencaminhados — fecha o K8.
//
// `HttpContext.Connection.RemoteIpAddress` devolve o endereço de quem
// estabelece a ligação TCP. Atrás do front-end do App Service, isso é o
// balanceador, não o cliente — e o IP guardado em `user_session` deixava de
// identificar quem se autenticou, o que esvaziava o requisito de auditoria
// BR-9.
//
// **Tem de vir antes de tudo o que lê o IP**, incluindo a autenticação: o
// `AuditContext` é construído a partir do `HttpContext` já processado.
//
// `KnownNetworks` e `KnownProxies` são limpos de propósito. Aceitar
// `X-Forwarded-For` de qualquer origem permitiria a um cliente forjar o
// próprio endereço — trocaria um registo inútil por um falsificável, que é
// pior. Em Azure App Service a confiança vem de a plataforma reescrever o
// cabeçalho no seu front-end, ao qual só ela tem acesso; fora dessa
// topologia, esta configuração tem de ser reavaliada.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        KnownNetworks = { },
        KnownProxies = { },
        // Um só salto: o front-end do App Service. Aceitar mais permitiria a
        // um cliente prefixar a cadeia com endereços à escolha.
        ForwardLimit = 1,
    });
}

// A ordem importa: autenticar (quem é) antes de autorizar (pode fazer isto).
app.UseAuthentication();
app.UseAuthorization();

app.MapIdentityModule();
app.MapAuditModule();
app.MapDocumentsModule();
app.MapHrModule();
app.MapNotificationsModule();

// Verifica que a aplicação está viva e que alcança a base de dados.
app.MapGet("/health", async (RivoIdentityDbContext db, CancellationToken ct) =>
{
    var reachable = await db.Database.CanConnectAsync(ct);
    return reachable
        ? Results.Ok(new { status = "ok", database = "up" })
        : Results.Problem("Base de dados inalcançável.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

