using Microsoft.Extensions.Options;

namespace Rivo.Api.Cors;

/// <summary>
/// Origens de browser autorizadas a chamar a API (ADR-033).
///
/// <para>
/// Lista separada por vírgulas, e não um array indexado como
/// <c>Bootstrap:Users</c>: isto vive no <c>.env</c> da VPS, escrito à mão, e
/// <c>CORS_ALLOWED_ORIGINS=a,b</c> é bastante menos fácil de errar do que três
/// linhas de <c>Cors__AllowedOrigins__0</c>.
/// </para>
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Origens exactas, separadas por vírgulas. Ex.: <c>https://app.rivo.ao,http://localhost:5173</c>.</summary>
    public string? AllowedOrigins { get; init; }

    /// <summary>
    /// As origens já divididas, sem espaços e sem barra final.
    ///
    /// <para>
    /// A barra final importa: o browser compara a origem por igualdade
    /// textual, e <c>https://app.rivo.ao/</c> nunca casa com o
    /// <c>Origin: https://app.rivo.ao</c> que ele próprio envia. É um erro de
    /// configuração que não dá mensagem nenhuma — só pedidos bloqueados.
    /// </para>
    /// </summary>
    public string[] Origins =>
        string.IsNullOrWhiteSpace(AllowedOrigins)
            ? []
            : [.. AllowedOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(origin => origin.TrimEnd('/'))];
}

public static class BrowserClientCors
{
    public const string PolicyName = "browser-clients";

    public static IServiceCollection AddBrowserClientCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName));

        var origins = configuration.GetSection(CorsOptions.SectionName)
            .Get<CorsOptions>()?.Origins ?? [];

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                // Sem origens configuradas, a política não autoriza ninguém —
                // que é o comportamento certo por omissão. Um SPA servido pelo
                // mesmo domínio que a API não precisa de CORS de todo.
                return;
            }

            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()

                // Sem `AllowCredentials`, e é decisão, não esquecimento.
                //
                // O ADR-013 escolheu JWT bearer precisamente para não usar
                // cookies: o token viaja no cabeçalho `Authorization`, que o
                // `AllowAnyHeader` acima já cobre. Ligar credenciais não
                // acrescentaria nada e proibiria para sempre o uso de `*`
                // em qualquer campo desta política.
                //
                // Se um dia se voltar a cookies, isto muda — e nessa altura
                // vem CSRF atrás, que o ADR-013 documenta ter evitado.

                // O cliente precisa de ler o cabeçalho para saber quando parar
                // de tentar; sem isto o browser esconde-o do JavaScript.
                .WithExposedHeaders("WWW-Authenticate");
        }));

        return services;
    }

    /// <summary>
    /// Regista a política e avisa quando ela não autoriza ninguém.
    ///
    /// <para>
    /// <strong>Tem de vir antes de <c>UseAuthentication</c>.</strong> O pedido
    /// de verificação prévia (<c>OPTIONS</c>) que o browser envia não leva
    /// cabeçalho <c>Authorization</c>: apanhado pela autenticação, seria
    /// recusado com 401 antes de o CORS lhe responder, e o pedido verdadeiro
    /// nunca chegaria a sair.
    /// </para>
    /// </summary>
    public static WebApplication UseBrowserClientCors(this WebApplication app)
    {
        var origins = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value.Origins;

        if (origins.Length == 0)
        {
            // Não falha o arranque: uma API servida no mesmo domínio que o
            // cliente é uma configuração legítima. Mas falhar em silêncio
            // deixaria um frontend a dar erros de rede sem causa visível.
            app.Logger.LogWarning(
                "CORS sem origens configuradas. Pedidos de browser vindos de outro domínio " +
                "serão bloqueados. Definir {Section}:{Key} se houver frontend noutra origem.",
                CorsOptions.SectionName,
                nameof(CorsOptions.AllowedOrigins));
        }
        else
        {
            app.Logger.LogInformation("CORS activo para: {Origins}", string.Join(", ", origins));
        }

        app.UseCors(PolicyName);

        return app;
    }
}
