using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Rivo.Api.Http;

namespace Rivo.Api.Tests;

/// <summary>
/// O teste que faltava quando o K8 foi dado como fechado, a 2026-08-16.
///
/// <para>
/// O código dizia `KnownNetworks = { }` num inicializador de objecto, o que
/// parece limpar e não limpa: as propriedades são só de leitura, `= { }` é um
/// inicializador de colecção, e chamar `Add` zero vezes deixa os valores por
/// omissão onde estavam. O middleware continuava a confiar apenas no loopback
/// e, atrás do reverse proxy, descartava os cabeçalhos sem dizer nada.
/// </para>
///
/// <para>
/// Custou duas investigações e um diagnóstico errado — atribuí-lo ao nome do
/// ambiente — porque os sintomas eram os mesmos: IP do proxy nas sessões e na
/// trilha, e `http` anunciado sobre uma ligação https.
/// </para>
/// </summary>
public sealed class ProxyHeadersTests
{
    [Fact]
    public void As_listas_de_confianca_ficam_mesmo_vazias()
    {
        var opcoes = ProxyHeaders.Options();

        // Se alguma destas voltar a ter o loopback, o middleware volta a
        // recusar o cabeçalho vindo do container do proxy.
        Assert.Empty(opcoes.KnownIPNetworks);
        Assert.Empty(opcoes.KnownProxies);
    }

    [Fact]
    public void O_defeito_original_nao_limpava_nada()
    {
        // Demonstra a armadilha, para que ninguém a reintroduza a achar que é
        // equivalente: este é o código que estava em produção.
        var comoEstava = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            KnownProxies = { },
        };

        Assert.Contains(IPAddress.IPv6Loopback, comoEstava.KnownProxies);
    }

    [Fact]
    public void Le_o_endereco_e_o_esquema_do_cliente_e_so_um_salto()
    {
        var opcoes = ProxyHeaders.Options();

        // `XForwardedProto` não é acessório: sem ele `Request.Scheme` fica
        // `http` atrás do proxy que termina o TLS.
        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));

        // Um salto só. Aceitar mais deixaria um cliente prefixar a cadeia com
        // endereços à escolha.
        Assert.Equal(1, opcoes.ForwardLimit);
    }
}
