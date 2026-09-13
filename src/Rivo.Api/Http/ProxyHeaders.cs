using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Rivo.Api.Http;

/// <summary>
/// Opções dos cabeçalhos reencaminhados — o que fecha o K8.
///
/// <para>
/// <strong>Existe como método e não como inicializador inline por causa de um
/// defeito que sobreviveu a duas investigações.</strong> O código era este:
/// </para>
///
/// <code>
/// new ForwardedHeadersOptions
/// {
///     KnownNetworks = { },   // NÃO limpa nada
///     KnownProxies  = { },   // NÃO limpa nada
/// }
/// </code>
///
/// <para>
/// `KnownIPNetworks` e `KnownProxies` são propriedades <em>só de leitura</em>,
/// pelo que `= { }` só pode ser um <strong>inicializador de colecção</strong>:
/// chama `Add` zero vezes e deixa os valores por omissão — `::1/128` e `::1` —
/// exactamente onde estavam. A intenção lia-se como «listas vazias»; o efeito
/// era «confia apenas no loopback».
/// </para>
///
/// <para>
/// Consequência: atrás do reverse proxy, o par da ligação é o container do
/// Caddy e não o loopback, o middleware conclui que o remetente não é de
/// confiança e <strong>descarta os cabeçalhos sem um aviso</strong>. O IP
/// guardado continuava a ser o do proxy, e `Request.Scheme` continuava `http`
/// sobre uma ligação https.
/// </para>
///
/// <para>
/// <strong>Com as duas listas mesmo vazias, o cabeçalho é aceite de qualquer
/// origem — e isso só é seguro porque não há outra origem.</strong> No
/// deployment em VPS (ADR-031) o container não publica porto nenhum no host:
/// vive numa rede interna do Docker e o único caminho até ele é o reverse
/// proxy, que reescreve `X-Forwarded-For`. Publicar o porto directamente, ou
/// pôr a API na Internet sem proxy à frente, torna o endereço registado
/// falsificável por qualquer cliente — e obriga a reavaliar isto.
/// </para>
/// </summary>
public static class ProxyHeaders
{
    public static ForwardedHeadersOptions Options()
    {
        var opcoes = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,

            // Um só salto: o reverse proxy da VPS. Aceitar mais permitiria a um
            // cliente prefixar a cadeia com endereços à escolha.
            ForwardLimit = 1,
        };

        // `.Clear()` e não `= { }`. É esta a linha que o defeito não tinha.
        opcoes.KnownIPNetworks.Clear();
        opcoes.KnownProxies.Clear();

        return opcoes;
    }
}
