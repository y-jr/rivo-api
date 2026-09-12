using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Rivo.Api.RateLimiting;

/// <summary>
/// Tecto de pedidos nas rotas que autenticam, por endereço de origem.
///
/// <para>
/// <strong>É a segunda metade do travão à força bruta.</strong> A primeira é o
/// bloqueio por tentativas falhadas, que fecha o ataque a <em>uma</em> conta:
/// cinco enganos e a conta fica quinze minutos fechada. O que o bloqueio não
/// cobre é varrer <em>muitas</em> contas com uma password comum — cada conta
/// leva uma tentativa só, nenhuma chega ao limite, e ninguém bloqueia. Nem
/// cobre o custo de processar as tentativas: cada verificação de password é uma
/// derivação de chave deliberadamente cara, e é dinheiro a arder mesmo quando
/// todas falham.
/// </para>
///
/// <para>
/// <strong>O endereço é o do cliente, não o do proxy.</strong> Depende de
/// `UseForwardedHeaders`, que o `Program` aplica fora de Development e que já
/// está configurado para confiar num salto — o do reverse proxy da VPS. Em
/// Development não há proxy e o endereço já é directo. Se algum dia a API for
/// exposta sem proxy à frente, essa configuração deixa de ser segura e esta
/// partição deixa de valer com ela.
/// </para>
/// </summary>
public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Pedidos por janela e por endereço. <c>0</c> desliga o tecto — e é o que
    /// o ambiente de verificação usa, porque as suites autenticam dezenas de
    /// vezes por minuto a partir de um endereço só, que não é a forma de
    /// nenhum cliente real.
    /// </summary>
    public int AuthenticationPermitLimit { get; init; } = 20;

    /// <summary>Duração da janela, em segundos.</summary>
    public int AuthenticationWindowSeconds { get; init; } = 60;
}

public static class AuthenticationRateLimiter
{
    /// <summary>Nome da política a pendurar nas rotas com <c>RequireRateLimiting</c>.</summary>
    public const string PolicyName = "autenticacao";

    public static IServiceCollection AddAuthenticationRateLimiter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AuthenticationRateLimitOptions>()
            .Bind(configuration.GetSection(AuthenticationRateLimitOptions.SectionName));

        var options = configuration
            .GetSection(AuthenticationRateLimitOptions.SectionName)
            .Get<AuthenticationRateLimitOptions>() ?? new AuthenticationRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            // 429 e não 503: o pedido está bem formado e a culpa é do ritmo,
            // não do servidor. `Retry-After` diz quando vale a pena voltar —
            // sem isso, um cliente bem comportado não tem como saber se espera
            // um segundo ou uma hora.
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)espera.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(PolicyName, http =>
            {
                // Zero desliga. Devolver `NoLimiter` é o idioma do próprio
                // framework para "esta partição não tem tecto", e evita ter de
                // tirar o `RequireRateLimiting` das rotas conforme o ambiente —
                // a rota é sempre a mesma, muda só a configuração.
                if (options.AuthenticationPermitLimit <= 0)
                {
                    return RateLimitPartition.GetNoLimiter("desligado");
                }

                // Sem endereço conhecido, todos caem no mesmo balde. É o
                // comportamento conservador: mais vale um tecto partilhado do
                // que nenhum, e na prática só acontece em pedidos que não vêm
                // de uma ligação de rede normal.
                var chave = http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

                return RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AuthenticationPermitLimit,
                    Window = TimeSpan.FromSeconds(options.AuthenticationWindowSeconds),

                    // Sem fila: quem excede é recusado de imediato. Pôr pedidos
                    // de autenticação em espera só transferiria o custo do
                    // atacante para a memória do servidor.
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    /// <summary>
    /// Regista o middleware e avisa quando o tecto está desligado.
    ///
    /// <para>
    /// <strong>Tem de vir depois do CORS.</strong> O pedido de verificação
    /// prévia (<c>OPTIONS</c>) do browser não deve gastar orçamento nenhum, e
    /// um 429 sem cabeçalhos de CORS chega ao JavaScript como erro de rede sem
    /// causa visível.
    /// </para>
    /// </summary>
    public static WebApplication UseAuthenticationRateLimiter(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<AuthenticationRateLimitOptions>>().Value;

        if (options.AuthenticationPermitLimit <= 0)
        {
            app.Logger.LogWarning(
                "Tecto de pedidos de autenticação desligado ({Section}:{Key} = 0). " +
                "É a configuração do ambiente de verificação, não de produção.",
                AuthenticationRateLimitOptions.SectionName,
                nameof(AuthenticationRateLimitOptions.AuthenticationPermitLimit));
        }
        else
        {
            app.Logger.LogInformation(
                "Tecto de pedidos de autenticação: {Limite} por {Janela}s e por endereço.",
                options.AuthenticationPermitLimit,
                options.AuthenticationWindowSeconds);
        }

        app.UseRateLimiter();

        return app;
    }
}
