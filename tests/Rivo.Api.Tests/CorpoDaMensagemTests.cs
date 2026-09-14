using MimeKit;
using Rivo.Api.Notifications;
using Rivo.Notifications.Domain;

namespace Rivo.Api.Tests;

/// <summary>
/// O desenho da mensagem de correio.
///
/// <para>
/// Verifica-se aqui porque é presentation, e no correio a presentation tem
/// consequências que não se vêem: um botão que não abre, um acento que sai como
/// entidade, ou marcação injectada pelo nome de um perfil.
/// </para>
/// </summary>
public sealed class CorpoDaMensagemTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private const string Destino = "https://rivo-lac.vercel.app/convite?u=abc&t=xyz";

    private static MimeMessage Montar(string corpo, string? url = Destino, string? etiqueta = "Escolher a minha password")
    {
        var notificacao = Notification.Create(
            Guid.CreateVersion7(), "identity.user_invited", "Tem acesso ao Rivo",
            corpo, sendEmail: true, Agora, url, etiqueta);

        return new MimeMessage { Body = CorpoDaMensagem.Construir(notificacao) };
    }

    [Fact]
    public void Sai_nas_duas_versoes_do_mesmo_conteudo()
    {
        var m = Montar("Primeiro parágrafo.\n\nSegundo parágrafo.");

        // `multipart/alternative`: quem lê em texto simples não fica com uma
        // versão degradada, fica com a mesma mensagem noutro registo.
        Assert.NotNull(m.TextBody);
        Assert.NotNull(m.HtmlBody);
        Assert.Contains("Primeiro parágrafo.", m.TextBody);
        Assert.Contains("Primeiro parágrafo.", m.HtmlBody);
    }

    [Fact]
    public void O_botao_leva_a_etiqueta_e_o_destino()
    {
        var html = Montar("Corpo.").HtmlBody;

        // Âncora e não `<button>`: um `<button>` não é clicável no correio.
        Assert.Contains($"href=\"{Destino.Replace("&", "&amp;")}\"", html);
        Assert.Contains("Escolher a minha password", html);
        Assert.Contains("<a ", html);
        Assert.DoesNotContain("<button", html);
    }

    [Fact]
    public void O_destino_aparece_tambem_por_extenso()
    {
        var m = Montar("Corpo.");

        // Em texto simples não há botão que o esconda. E no HTML fica em
        // pequeno, porque um botão que não abre — cliente antigo, mensagem
        // reencaminhada — deixaria quem recebe sem nada para onde ir.
        Assert.Contains(Destino, m.TextBody);
        Assert.Contains("copie esta ligação", m.HtmlBody);
    }

    [Fact]
    public void Sem_accao_nao_ha_botao_nem_ligacao_solta()
    {
        var m = Montar("Só um aviso.", url: null, etiqueta: null);

        Assert.DoesNotContain("copie esta ligação", m.HtmlBody);
        Assert.Contains("Só um aviso.", m.TextBody);
    }

    [Fact]
    public void O_corpo_e_escapado_antes_de_entrar_na_marcacao()
    {
        // O corpo é escrito por um módulo de negócio e pode levar o nome de um
        // perfil ou de uma pessoa. Nada disto entra em HTML por escapar.
        var html = Montar("Perfil <script>alert(1)</script> & companhia.").HtmlBody;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Uma_linha_em_branco_separa_paragrafos()
    {
        var html = Montar("Um.\n\nDois.\n\nTres.").HtmlBody;

        // Cada um no seu parágrafo, e não um bloco só com quebras perdidas:
        // no correio, `white-space` não é de confiança.
        Assert.Contains(">Um.</p>", html);
        Assert.Contains(">Dois.</p>", html);
        Assert.Contains(">Tres.</p>", html);
    }
}
