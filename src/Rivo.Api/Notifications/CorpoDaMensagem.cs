using System.Text;
using MimeKit;
using MimeKit.Text;
using Rivo.Notifications.Domain;

namespace Rivo.Api.Notifications;

/// <summary>
/// Desenha a mensagem de correio a partir de uma notificação.
///
/// <para>
/// <strong>Duas versões da mesma coisa, e não uma escolha entre elas.</strong> A
/// mensagem sai como <c>multipart/alternative</c>: quem lê em texto simples
/// recebe o corpo tal e qual, com a ligação escrita por extenso; quem lê em HTML
/// recebe o mesmo texto com o destino desenhado como botão. Nenhum dos dois é
/// degradado — são o mesmo conteúdo em dois registos.
/// </para>
///
/// <para>
/// <strong>Vive aqui e não em <c>notifications</c>.</strong> O módulo guarda
/// texto e um destino; como isso se pinta é assunto do canal. É a mesma razão
/// por que o <c>Message</c> nunca leva marcação: é o corpo que a aplicação
/// mostra na lista de notificações, onde HTML apareceria como etiquetas à vista.
/// </para>
///
/// <para>
/// <strong>O HTML é de correio, e não de página.</strong> Tabelas e estilos em
/// linha, porque metade dos clientes ainda descarta folhas de estilo e nenhum
/// implementa flex. Um botão «à prova» é uma âncora com <c>padding</c> dentro de
/// uma célula — não um <c>&lt;button&gt;</c>, que não é clicável no correio.
/// </para>
/// </summary>
public static class CorpoDaMensagem
{
    // Cores do manual de identidade. As quatro paragens do gradiente são
    // decorativas e só: o manual mede branco sobre cada uma e nenhuma chega a
    // 4,5:1 — a mais escura fica em 4,68:1. Por isso o botão **não** usa nenhuma
    // delas. Usa o índigo aplicado do tema claro, que dá 5,55:1 com branco.
    private const string Ciano = "#2CB8E6";
    private const string Indigo = "#5A75EB";
    private const string Corporativo = "#6467EC";
    private const string Roxo = "#934EE3";

    private const string Botao = "#5558D9";
    private const string Tinta = "#101420";
    private const string TintaSuave = "#4C5468";
    private const string TintaTenue = "#767E93";
    private const string Fundo = "#F2F3F8";
    private const string Cartao = "#FFFFFF";
    private const string Fio = "#DFE2EC";

    private const string Familia =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    public static MimeEntity Construir(Notification notification)
    {
        var corpo = new BodyBuilder
        {
            TextBody = EmTexto(notification),
            HtmlBody = EmHtml(notification),
        };

        return corpo.ToMessageBody();
    }

    /// <summary>
    /// O destino escrito por extenso, porque em texto simples não há botão que o
    /// esconda — e porque é isto que sobra se o HTML for bloqueado.
    /// </summary>
    private static string EmTexto(Notification notification)
    {
        var texto = new StringBuilder(notification.Message);

        if (notification.ActionUrl is { } destino)
        {
            texto.Append("\n\n")
                 .Append(notification.ActionLabel)
                 .Append(":\n")
                 .Append(destino);
        }

        texto.Append("\n\n—\nMensagem automática do Rivo.");
        return texto.ToString();
    }

    private static string EmHtml(Notification notification)
    {
        var titulo = Escapar(notification.Title);
        var html = new StringBuilder(2048);

        html.Append("<!doctype html><html lang=\"pt\"><head>")
            .Append("<meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            // Diz aos clientes que a mensagem é desenhada para fundo claro. Sem
            // isto, o modo escuro de alguns inverte as cores por conta própria e
            // desfaz o contraste que foi medido.
            .Append("<meta name=\"color-scheme\" content=\"light\">")
            .Append("<meta name=\"supported-color-schemes\" content=\"light\">")
            .Append("<title>").Append(titulo).Append("</title>")
            .Append("</head>")
            .Append($"<body style=\"margin:0;padding:0;background:{Fundo};\">");

        // Linha de pré-visualização: é o que a caixa de entrada mostra a seguir
        // ao assunto. Escondida no corpo, para não aparecer duas vezes.
        html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">")
            .Append(Escapar(PrimeiraLinha(notification.Message)))
            .Append("</div>");

        html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:{Fundo};\">")
            .Append("<tr><td align=\"center\" style=\"padding:32px 16px;\">")
            .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
            .Append($"style=\"width:100%;max-width:600px;background:{Cartao};border:1px solid {Fio};border-radius:12px;overflow:hidden;\">");

        // Faixa da marca: quatro células, uma por paragem. Um gradiente CSS
        // verdadeiro não sobrevive ao Outlook; quatro cores sólidas sobrevivem
        // em todo o lado e lêem-se como a mesma faixa.
        html.Append("<tr><td style=\"padding:0;font-size:0;line-height:0;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
            .Append($"<td width=\"25%\" height=\"4\" style=\"background:{Ciano};font-size:0;line-height:0;\">&nbsp;</td>")
            .Append($"<td width=\"25%\" height=\"4\" style=\"background:{Indigo};font-size:0;line-height:0;\">&nbsp;</td>")
            .Append($"<td width=\"25%\" height=\"4\" style=\"background:{Corporativo};font-size:0;line-height:0;\">&nbsp;</td>")
            .Append($"<td width=\"25%\" height=\"4\" style=\"background:{Roxo};font-size:0;line-height:0;\">&nbsp;</td>")
            .Append("</tr></table></td></tr>");

        // Marca. Texto e não imagem, de propósito: a maioria dos clientes
        // bloqueia imagens por omissão, e uma marca que não carrega é pior do
        // que uma marca escrita.
        html.Append("<tr><td style=\"padding:28px 32px 0;\">")
            .Append($"<span style=\"font-family:{Familia};font-size:22px;font-weight:700;letter-spacing:-.04em;color:{Tinta};\">")
            .Append($"ri<span style=\"color:{Botao};\">vo</span></span>")
            .Append("</td></tr>");

        html.Append("<tr><td style=\"padding:20px 32px 0;\">")
            .Append($"<h1 style=\"margin:0;font-family:{Familia};font-size:24px;line-height:1.25;font-weight:700;letter-spacing:-.02em;color:{Tinta};\">")
            .Append(titulo).Append("</h1></td></tr>");

        foreach (var paragrafo in Paragrafos(notification.Message))
        {
            html.Append("<tr><td style=\"padding:16px 32px 0;\">")
                .Append($"<p style=\"margin:0;font-family:{Familia};font-size:15px;line-height:1.6;color:{TintaSuave};\">")
                .Append(Escapar(paragrafo).Replace("\n", "<br>"))
                .Append("</p></td></tr>");
        }

        if (notification.ActionUrl is { } destino)
        {
            var href = Escapar(destino);

            html.Append("<tr><td style=\"padding:28px 32px 0;\">")
                .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
                .Append($"<td style=\"background:{Botao};border-radius:8px;\">")
                .Append($"<a href=\"{href}\" style=\"display:inline-block;padding:14px 30px;font-family:{Familia};")
                .Append("font-size:15px;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:8px;\">")
                .Append(Escapar(notification.ActionLabel!))
                .Append("</a></td></tr></table></td></tr>");

            // A ligação por extenso fica na mesma, em pequeno. Um botão que não
            // abre — cliente antigo, correio reencaminhado, texto colado — deixa
            // quem recebe sem nada para onde ir.
            html.Append("<tr><td style=\"padding:20px 32px 0;\">")
                .Append($"<p style=\"margin:0;font-family:{Familia};font-size:12px;line-height:1.6;color:{TintaTenue};\">")
                .Append("Se o botão não abrir, copie esta ligação para o navegador:<br>")
                .Append($"<span style=\"color:{TintaSuave};word-break:break-all;\">").Append(href).Append("</span>")
                .Append("</p></td></tr>");
        }

        html.Append("<tr><td style=\"padding:28px 32px 28px;\">")
            .Append($"<div style=\"border-top:1px solid {Fio};padding-top:16px;\">")
            .Append($"<p style=\"margin:0;font-family:{Familia};font-size:12px;line-height:1.6;color:{TintaTenue};\">")
            .Append("Mensagem automática do Rivo. Não é preciso responder.")
            .Append("</p></div></td></tr>");

        html.Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    /// <summary>Uma linha em branco separa parágrafos, como no texto escrito.</summary>
    private static IEnumerable<string> Paragrafos(string mensagem) =>
        (mensagem ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string PrimeiraLinha(string mensagem) =>
        Paragrafos(mensagem).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Escapa os cinco caracteres com significado em HTML, e mais nenhum.
    ///
    /// <para>
    /// <c>WebUtility.HtmlEncode</c> faria mais do que é preciso: converte cada
    /// caracter fora de ASCII numa entidade numérica, e em português isso
    /// transforma metade do texto em <c>&amp;#225;</c>. O documento declara UTF-8,
    /// logo os acentos viajam tal e qual.
    /// </para>
    /// o nome de um perfil ou de uma pessoa — nada disto entra em marcação sem
    /// passar por aqui.
    /// </summary>
    private static string Escapar(string? valor) =>
        (valor ?? string.Empty)
            .Replace("&", "&amp;")   // primeiro, senão re-escapa os próprios
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
}
