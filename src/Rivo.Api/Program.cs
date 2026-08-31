using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Rivo.Api.Composition;
using Rivo.Api.Cors;
using Rivo.Api.Errors;
using Rivo.Api.OpenApi;
using Rivo.Audit.Api;
using Rivo.Commercial.Api;
using Rivo.Commercial.Infrastructure;
using Rivo.Procurement.Api;
using Rivo.Procurement.Infrastructure;
using Rivo.Audit.Infrastructure;
using Rivo.Documents.Api;
using Rivo.Documents.Infrastructure;
using Rivo.Finance.Api; 
using Rivo.Finance.Infrastructure;
using Rivo.Fiscal.Api;
using Rivo.Fiscal.Infrastructure;
using Rivo.Approval.Api;
using Rivo.Approval.Infrastructure;
using Rivo.Hr.Api;
using Rivo.Finance.Application.Abstractions;
using Rivo.Hr.Application.Abstractions;
using Rivo.Procurement.Application.Abstractions;
using Rivo.Hr.Infrastructure;
using Rivo.Identity.Api;
using Rivo.Notifications.Api;
using Rivo.Notifications.Infrastructure;
using Rivo.Identity.Infrastructure;
using Rivo.Identity.Infrastructure.Persistence;
using Rivo.Payroll.Api;
using Rivo.Payroll.Application.Abstractions;
using Rivo.Payroll.Infrastructure;
using Rivo.Projects.Api;
using Rivo.Projects.Infrastructure;
using Rivo.Inventory.Api;
using Rivo.Inventory.Infrastructure;
using Rivo.Fleet.Api;
using Rivo.Fleet.Infrastructure;
using Rivo.Settings.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

// Origens de browser autorizadas (ADR-033). Concern do host e não de módulo:
// é o processo inteiro que é chamado de fora, não cada módulo por si.
builder.Services.AddBrowserClientCors(builder.Configuration);

// Colisão de concorrência optimista traduzida em 409 (ADR-035, fecha o K15).
//
// Aqui e não em cada módulo: nenhuma camada Application referencia o EF Core,
// logo não há onde apanhar a excepção dentro do módulo sem lhe arrastar a
// infraestrutura. Ver Errors/ConcurrencyConflictHandler.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ConcurrencyConflictHandler>();

// Repõe o 400 que o middleware acima passou a esconder: registar
// `UseExceptionHandler` põe-se à frente do Kestrel, que era quem reconhecia a
// `BadHttpRequestException` e respondia com o código dela. Ver BadRequestHandler.
builder.Services.AddExceptionHandler<BadRequestHandler>();

// Cada módulo regista os seus próprios serviços e policies. A ordem segue as
// dependências de contrato: `identity` consome os contratos de `audit` e `hr`.
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddFiscalModule(builder.Configuration);
builder.Services.AddCommercialModule(builder.Configuration);
builder.Services.AddProcurementModule(builder.Configuration);
builder.Services.AddFinanceModule(builder.Configuration);
builder.Services.AddHrModule(builder.Configuration);
builder.Services.AddApprovalModule(builder.Configuration);
builder.Services.AddPayrollModule(builder.Configuration);
builder.Services.AddProjectsModule(builder.Configuration);
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddFleetModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);

// Camada de composição, não módulo (ADR-041) — sem base de dados, sem
// migração. Depois de `identity` e `approval`: compõe os contratos dos
// dois, e as policies de autorização que usa (`identity.roles.read`,
// `approval.policies.read`) nascem do `AddAuthorization` de cada um deles.
builder.Services.AddSettingsModule();

// Apresenta `hr` a `approval`, sem que nenhum dos dois conheça o outro.
//
// É trabalho de composition root, e tem de ser feito aqui por desenho: `hr`
// declara `IHrApprovalSubmission` nas suas palavras, e referenciar
// `Rivo.Approval.Contracts` a partir de `hr` recriaria o ciclo `hr ↔ approval`
// que o ADR-015 §R1 deixou por fechar. Ver Composition/PositionApprovalSubmission.
builder.Services.AddScoped<IHrApprovalSubmission, HrApprovalSubmission>();

// E `finance` a `approval`, pela mesma inversão. Aqui não é só higiene: BR-8
// fará `approval` ler `finance`, e uma referência directa traria de volta o
// ciclo que o ADR-034 fechou. Ver Composition/FinancePaymentApproval.
builder.Services.AddScoped<IPaymentApproval, FinancePaymentApproval>();

// E `procurement`, para a requisição interna. Aqui não há ciclo a quebrar —
// `approval` não lê `procurement` — mas mantém-se a inversão para que o módulo
// continue a não saber qual é o motor de governança.
// Ver Composition/ProcurementApprovalSubmission.
builder.Services.AddScoped<IProcurementApprovalSubmission, ProcurementApprovalSubmission>();

// E `payroll`, mesmo desenho — ver Composition/PayrollApprovalSubmission.
// Esqueleto: sem cálculo de IRT/INSS, a folha submete-se pelo total bruto.
builder.Services.AddScoped<IPayrollApprovalSubmission, PayrollApprovalSubmission>();

var app = builder.Build();

// Documentação e interface da API, por interruptor explícito — ADR-038.
//
// Expor a superfície da API alarga o que um atacante sabe sem ter de
// adivinhar, e por isso a omissão continua a ser não expor fora de
// desenvolvimento. Mas **quem opera o ambiente tem de poder dizer que sim**
// sem para isso ter de mentir sobre o ambiente.
//
// Pôr `ASPNETCORE_ENVIRONMENT=Development` num ambiente publicado abre o
// Swagger, sim — e traz atrás duas coisas que ninguém pediu: o
// `UseForwardedHeaders` deixa de correr, o que **reabre o K8 em silêncio** e
// volta a guardar em `user_session` o IP do proxy em vez do do cliente; e o
// `WebApplication` acrescenta a página de excepções de desenvolvimento à
// frente de todo o pipeline, que devolve stack trace e código-fonte a quem
// provocar um erro não tratado.
//
// Mesmo desenho de `Database:MigrateOnStartup` (ADR-030): a decisão é escrita
// no compose de quem opera o ambiente, não é efeito colateral do nome dele.
if (app.Configuration.GetValue("OpenApi:Expose", app.Environment.IsDevelopment()))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Rivo API"));
}

// Migração no arranque, por interruptor explícito de configuração — ADR-030.
//
// O ADR-020 tinha-a tirado do arranque e feito dela passo de pipeline, por
// duas razões: várias instâncias competiriam pelo mesmo schema, e uma migração
// destrutiva correria sem ninguém a aprovar.
//
// O deployment em VPS (ADR-031) não tem pipeline com acesso à base de dados —
// o CD faz `git pull` e `docker compose up`, e mais nada. A migração tem de
// voltar ao arranque, e volta com as duas razões tratadas:
//
//   - **Instâncias concorrentes:** é uma só, por desenho do compose. Se um dia
//     forem várias, este interruptor desliga-se e a migração volta a ser passo
//     próprio, sem tocar em código.
//   - **Destrutivo sem aprovação:** o interruptor é a aprovação. Não é efeito
//     colateral de `ASPNETCORE_ENVIRONMENT`, é uma decisão escrita no
//     `docker-compose.yml` de quem opera o ambiente.
//
// A ordem importa: `hr` depois de `documents`, porque a chave estrangeira
// entre schemas exige que `documents.document` já exista; `identity` por
// último, porque o seu seed depende dos schemas dos outros.
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    var limite = TimeSpan.FromSeconds(app.Configuration.GetValue("Database:StartupTimeoutSeconds", 180));

    // **Esperar por uma ligação real antes de migrar.** Sem isto há uma corrida
    // que só aparece em reinícios: `docker compose restart` não respeita
    // `depends_on`, a API sobe enquanto o SQL Server ainda recupera a base, e o
    // EF Core conclui que ela **não existe** — porque `Exists()` é "consegui
    // abrir ligação?" — e tenta criá-la. O `CREATE DATABASE` falha com o erro
    // 1801, que se repete tantas vezes quantas o prazo permitir sem nunca
    // resolver, e o arranque morre com a base perfeitamente saudável ao lado.
    //
    // Observado a 2026-08-25 numa corrida de `verify-payables`: 28 tentativas
    // seguidas de "Database 'rivo' already exists".
    //
    // **A sonda original abria a ligação já apontada ao nome da base, e isso
    // confundia duas causas com a mesma superfície.** "A base existe e está a
    // recuperar" é o caso acima, e passa sozinho — mas "a base nunca existiu"
    // tem o mesmo sintoma (login recusado) e **não passa nunca**: o SQL Server
    // recusa sempre abrir uma ligação cujo catálogo inicial não existe,
    // independentemente de quanto se espere. A sonda ficava presa mesmo antes
    // do passo que teria criado a base — que é a própria migração, mais abaixo.
    //
    // Observado a 2026-08-28 num volume novo, sem `rivo` nenhum: 139 tentativas
    // sem avançar, reinício do container, e o mesmo ciclo outra vez — para
    // sempre, porque nada além da migração cria a base, e a migração nunca
    // chegava a correr.
    //
    // A sonda passa a perguntar a `master` — que existe sempre — pelo estado
    // registado da base alvo, e distingue as três respostas possíveis:
    //
    //   - Sem linha nenhuma: a base nunca existiu. Segue-se para a migração,
    //     que a cria — não há nada aqui para esperar.
    //   - `ONLINE`: pronta. Segue-se.
    //   - Qualquer outro estado (`RECOVERING`, `RECOVERY_PENDING`, ...): ainda
    //     não — é o caso original que motivou a sonda. Repete-se.
    await AteABaseEstarProntaAsync(app, limite, async () =>
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RivoIdentityDbContext>();

        await EsperarBaseAlvoOuInexistenteAsync(db.Database.GetConnectionString()!);
    });

    await AteABaseEstarProntaAsync(
        app,
        limite,
        async () =>
        {
            await app.Services.MigrateAuditModuleAsync();
            await app.Services.MigrateDocumentsModuleAsync();
            await app.Services.MigrateNotificationsModuleAsync();
            await app.Services.MigrateFiscalModuleAsync();
            await app.Services.MigrateCommercialModuleAsync();
            await app.Services.MigrateProcurementModuleAsync();
            await app.Services.MigrateFinanceModuleAsync();
            await app.Services.MigrateHrModuleAsync();
            await app.Services.MigrateApprovalModuleAsync();
            await app.Services.MigratePayrollModuleAsync();
            await app.Services.MigrateProjectsModuleAsync();
            await app.Services.MigrateInventoryModuleAsync();
            await app.Services.MigrateFleetModuleAsync();
            await app.Services.MigrateIdentityModuleAsync();
        });
}

// Seed separado da migração — ADR-028.
//
// **Não acompanha a migração, e a distinção é o ponto.** Migrar
// automaticamente carrega os riscos acima; semear não: o seed é idempotente
// (ADR-016), nunca altera contas existentes e só acrescenta o que falta.
//
// Estarem juntos era acidente de implementação, e o preço apareceu no primeiro
// deployment: o ambiente ficou com as tabelas todas e nenhum Perfil de Acesso,
// porque o gate de ambiente levou o seed atrás da migração.
//
// Por omissão corre em tudo excepto `Production` — em produção, criar o
// primeiro administrador é acto deliberado. `Bootstrap:SeedOnStartup` permite
// dizer sim explicitamente, que é o que um ambiente novo em VPS precisa para
// nascer com administrador (ADR-031).
if (app.Configuration.GetValue("Bootstrap:SeedOnStartup", !app.Environment.IsProduction()))
{
    await app.Services.SeedIdentityModuleAsync();

    // Série de numeração por omissão (ADR-036). Idempotente: se já existir,
    // não lhe toca — e em particular não lhe recua o contador.
    await app.Services.SeedFinanceModuleAsync();
}

// Primeiro middleware do pipeline, para envolver tudo o que vem a seguir —
// incluindo a autenticação, que também escreve na base de dados (a sessão).
app.UseExceptionHandler();

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
// `KnownNetworks` e `KnownProxies` são limpos de propósito — com as duas
// listas vazias, o middleware aceita o cabeçalho de qualquer origem.
//
// **Isso só é seguro porque não há outra origem.** No deployment em VPS
// (ADR-031) o container não publica porto nenhum no host: está numa rede
// interna do Docker e o único caminho até ele é o reverse proxy, que reescreve
// `X-Forwarded-For`. Publicar o porto directamente, ou pôr a API na Internet
// sem proxy à frente, torna o endereço registado falsificável por qualquer
// cliente — e obriga a reavaliar esta configuração.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        KnownNetworks = { },
        KnownProxies = { },
        // Um só salto: o reverse proxy da VPS. Aceitar mais permitiria a um
        // cliente prefixar a cadeia com endereços à escolha.
        ForwardLimit = 1,
    });
}

// CORS antes da autenticação, e depois dos cabeçalhos reencaminhados.
//
// O pedido `OPTIONS` de verificação prévia não leva `Authorization`: se a
// autenticação o visse primeiro, respondia 401 e o pedido verdadeiro nunca
// chegava a sair do browser.
app.UseBrowserClientCors();

// A ordem importa: autenticar (quem é) antes de autorizar (pode fazer isto).
app.UseAuthentication();
app.UseAuthorization();

app.MapIdentityModule();
app.MapAuditModule();
app.MapDocumentsModule();
app.MapFiscalModule();
app.MapCommercialModule();
app.MapProcurementModule();
app.MapFinanceModule();
app.MapPayables();
app.MapLedger();
app.MapHrModule();
app.MapApprovalModule();
app.MapPayrollModule();
app.MapProjectsModule();
app.MapInventoryModule();
app.MapFleetModule();
app.MapNotificationsModule();
app.MapSettingsModule();

// Verifica que a aplicação está viva e que alcança a base de dados.
app.MapGet("/health", async (RivoIdentityDbContext db, CancellationToken ct) =>
{
    var reachable = await db.Database.CanConnectAsync(ct);
    return reachable
        ? Results.Ok(new { status = "ok", database = "up" })
        : Results.Problem("Base de dados inalcançável.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

/// <summary>
/// Corre a operação, repetindo-a enquanto a base de dados não estiver pronta.
///
/// <para>
/// <strong>Não substitui a resiliência de ligação do EF Core, complementa-a.</strong>
/// `EnableRetryOnFailure` repete o que a estratégia classifica como falha
/// transitória, e um servidor que ainda nem sequer abriu o porto não entra
/// nessa categoria: dá <c>SocketException</c>, que sobe de imediato e mata o
/// arranque.
/// </para>
///
/// <para>
/// É a diferença entre "a ligação caiu a meio" e "o servidor ainda não
/// atende" — e a segunda é o caso normal ao subir a stack, onde o container da
/// aplicação arranca em segundos e o do SQL Server demora dezenas deles.
/// `depends_on: service_healthy` cobre o `up`, mas não o `restart` nem o
/// reinício automático depois de uma falha.
/// </para>
///
/// <para>
/// <strong>Repete-se a migração inteira, e não uma sondagem contra o nome da
/// base.</strong> Uma sondagem que abra ligação já apontada ao nome
/// configurado bate no erro 4060 sempre que a base ainda não existe, e nesse
/// caso não há repetição que resolva — só a migração cria a base. Por isso a
/// sondagem que precede este passo (<see cref="EsperarBaseAlvoOuInexistenteAsync"/>)
/// pergunta a <c>master</c>, que existe sempre, e distingue "não existe ainda"
/// (segue-se, é a migração que a cria) de "existe mas está a recuperar"
/// (espera-se). Historial da distinção em <c>c3dd29d</c> e na correcção que se
/// lhe seguiu.
/// </para>
///
/// <para>
/// Repetir migrações é seguro: cada módulo consulta a sua tabela de histórico
/// e aplica só o que falta (ADR-020). Uma sequência interrompida a meio
/// retoma onde ficou.
/// </para>
///
/// <para>
/// <strong>Só se repete pelas falhas de arranque de ambiente.</strong> Um
/// erro de migração — SQL inválido, restrição violada — sobe à primeira, porque
/// esperar não o resolve. E a espera é limitada: ao fim do prazo, a excepção
/// mata o arranque. Um container que reinicia com erro visível é melhor do
/// que um processo vivo e eternamente à espera, que nenhuma sonda distingue
/// de saudável.
/// </para>
/// </summary>
/// <summary>
/// Lança enquanto a base configurada existir mas ainda não estiver
/// <c>ONLINE</c>. Regressa em silêncio quando está pronta, e também quando
/// ainda não existe de todo — nesse caso é a migração, e não esta sonda, que a
/// cria (ver o comentário acima de <see cref="AteABaseEstarProntaAsync"/>).
///
/// <para>
/// Pergunta-se a <c>master</c> em vez de abrir ligação directamente à base
/// alvo, porque <c>master</c> existe sempre. Uma ligação directa ao nome
/// configurado falha exactamente da mesma forma nos dois casos — "existe e
/// está a recuperar" e "nunca existiu" — e só o primeiro passa sozinho, por
/// definição. Distinguir os dois é o que evita o impasse: sem isto, a sonda
/// esperava para sempre por uma base que só a migração, três linhas abaixo,
/// teria criado.
/// </para>
/// </summary>
static async Task EsperarBaseAlvoOuInexistenteAsync(string connectionString)
{
    var alvo = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    var nomeDaBase = alvo.InitialCatalog;

    // A mesma ligação, salvo o catálogo inicial: credenciais e o resto da
    // configuração de rede continuam a ser os da base real.
    alvo.InitialCatalog = "master";

    try
    {
        await using var ligacao = new Microsoft.Data.SqlClient.SqlConnection(alvo.ConnectionString);
        await ligacao.OpenAsync();

        await using var comando = ligacao.CreateCommand();
        comando.CommandText = "SELECT state_desc FROM sys.databases WHERE name = @nome";
        comando.Parameters.AddWithValue("@nome", nomeDaBase);

        var estado = await comando.ExecuteScalarAsync() as string;

        // `null`: sem linha nenhuma — a base nunca existiu. Não há nada aqui
        // para esperar; regressa-se e segue-se para a migração.
        if (estado is not null && !string.Equals(estado, "ONLINE", StringComparison.Ordinal))
        {
            // Excepção e não `return`: é a repetição de AteABaseEstarProntaAsync
            // que faz a espera, e um regresso silencioso deixaria a migração
            // arrancar contra uma base ainda a recuperar.
            throw new InvalidOperationException(
                $"A base de dados ainda não aceita ligações (estado: {estado}).");
        }
    }
    catch (Exception excepcao) when (excepcao is not InvalidOperationException)
    {
        // O próprio `master` ainda não atende — o servidor está a arrancar.
        // Mesma condição, mensagem igual à que a sonda original lançava, para
        // que `VaiPassarSozinho` continue a classificá-la sem mudanças.
        throw new InvalidOperationException(
            "A base de dados ainda não aceita ligações.", excepcao);
    }
}

static async Task AteABaseEstarProntaAsync(WebApplication app, TimeSpan limite, Func<Task> operacao)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Rivo.Arranque");
    var fim = DateTimeOffset.UtcNow + limite;
    var tentativa = 0;

    while (true)
    {
        tentativa++;

        try
        {
            await operacao();
            return;
        }
        catch (Exception excepcao) when (VaiPassarSozinho(excepcao) && DateTimeOffset.UtcNow < fim)
        {
            logger.LogInformation(
                "Base de dados ainda não está pronta (tentativa {Tentativa}): {Motivo} A repetir.",
                tentativa,
                excepcao.Message);

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

/// <summary>
/// Distingue as falhas que a tentativa seguinte resolve das que não.
///
/// <para>
/// São duas, e ambas são de arranque de ambiente, não de esquema. Qualquer
/// outra coisa — credenciais recusadas, SQL inválido, restrição violada — sobe
/// à primeira, porque repetir não a resolve.
/// </para>
/// </summary>
static bool VaiPassarSozinho(Exception excepcao)
{
    for (var actual = excepcao; actual is not null; actual = actual.InnerException)
    {
        // 1. O servidor ainda não atende. A mensagem nem chegou a ser
        //    processada, e é por isso que se olha para a cadeia de excepções
        //    e não para um código de erro do motor.
        if (actual is System.Net.Sockets.SocketException)
        {
            return true;
        }

        // 2. `CREATE DATABASE` executado duas vezes, só no primeiro arranque
        //    contra um servidor vazio.
        //
        //    A estratégia de repetição do EF Core (`EnableRetryOnFailure`)
        //    repete comandos que classifica como transitórios — e a criação da
        //    base pode falhar *depois* de o servidor a ter criado. A repetição
        //    encontra-a lá e rebenta com o erro 1801, que a estratégia já não
        //    considera transitório.
        //
        //    ⚠ **Repetir nem sempre resolve**, e foi por isso que a sonda de
        //    ligação passou a correr antes da migração. Se o servidor atende
        //    mas ainda está a recuperar a base, o EF volta a concluir que ela
        //    não existe a cada tentativa, e a repetição esgota o prazo sem
        //    progresso. Isto fica como rede de segurança para o caso que
        //    descreve — duas criações no primeiro arranque — e não como a
        //    defesa principal.
        if (actual is Microsoft.Data.SqlClient.SqlException { Number: 1801 })
        {
            return true;
        }

        // 3. O servidor atende mas a base ainda não aceita ligações. É o que a
        //    sonda de arranque lança, e é exactamente a condição que se espera
        //    que passe sozinha.
        if (actual is InvalidOperationException
            && actual.Message.Contains("ainda não aceita ligações", StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}
