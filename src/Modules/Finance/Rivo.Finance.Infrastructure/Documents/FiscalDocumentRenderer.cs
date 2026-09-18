using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Rivo.Finance.Application.Abstractions;

namespace Rivo.Finance.Infrastructure.Documents;

/// <summary>
/// Compõe o PDF de um documento fiscal, com QuestPDF.
///
/// <para>
/// <strong>A biblioteca só é conhecida aqui.</strong> Acima desta classe existe
/// <see cref="IFiscalDocumentRenderer"/> e um <see cref="FiscalDocumentPrintout"/>
/// — trocar QuestPDF por outra coisa é reescrever este ficheiro e mais nada.
/// </para>
///
/// <para>
/// <strong>Nada aqui vai à base de dados nem ao relógio.</strong> Recebe o
/// printout e devolve bytes: o mesmo printout dá sempre o mesmo documento, que é
/// a propriedade de que uma segunda impressão precisa.
/// </para>
///
/// <para>
/// Não veio de graça. A primeira versão <em>parecia</em> determinística e não
/// era: o QuestPDF grava `/CreationDate` e `/ModDate` com o instante da
/// composição, e o teste que devia apanhá-lo passava localmente por as duas
/// composições caírem no mesmo segundo. Ver a nota em <c>WithMetadata</c>, em
/// <see cref="Render"/>.
/// </para>
/// </summary>
public sealed class FiscalDocumentRenderer : IFiscalDocumentRenderer
{
    /// <summary>
    /// A licença do QuestPDF, declarada uma vez.
    ///
    /// <para>
    /// <strong>Community.</strong> Os termos (versão 3.0, 6 de Julho de 2026)
    /// dão-na sem custo a organizações com receita anual bruta inferior a
    /// 1 000 000 USD, em base consolidada, que não sejam do sector público nem
    /// cotadas em bolsa — e permitem explicitamente aplicações comerciais e a
    /// redistribuição da biblioteca compilada dentro delas. É a categoria em que
    /// o Rivo cabe hoje.
    /// </para>
    ///
    /// <para>
    /// <strong>Se a receita passar esse limiar, a licença passa a paga.</strong>
    /// Fica escrito aqui, e não só no ADR, porque é a linha de código que a
    /// afirma: quem a ler daqui a dois anos tem de saber que é uma declaração
    /// com consequência contratual e não um detalhe de arranque.
    /// </para>
    ///
    /// <para>
    /// No construtor estático e não no <c>Program.cs</c> de propósito — assim
    /// vale também para quem usa o compositor nos testes, sem depender da ordem
    /// de arranque do host.
    /// </para>
    /// </summary>
    static FiscalDocumentRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>
    /// Formatação em pt-AO. Milhares com espaço, decimais com vírgula — a forma
    /// que se lê em Angola. Fixa, e não a cultura do processo: um documento não
    /// muda de aspecto por o servidor ter mudado de região.
    /// </summary>
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-AO");

    public byte[] Render(FiscalDocumentPrintout printout)
    {
        ArgumentNullException.ThrowIfNull(printout);

        var doc = printout.Document;

        // Há colunas de imposto quando há imposto. O recibo não tem — liquida
        // valores já tributados — e imprimir duas colunas de zeros só rouba
        // espaço a quem lê.
        var comImposto = doc.TaxTotal != 0m || doc.Lines.Any(l => l.TaxPercentage != 0m);

        return QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            // Sem `FontFamily`, e é decisão em vez de esquecimento.
            //
            // **Pedir Calibri rebentava no contentor.** A imagem de runtime do
            // .NET é Linux e não traz fontes da Microsoft, e o QuestPDF lança
            // `DocumentDrawingException` em vez de substituir em silêncio —
            // portanto o endpoint respondia 500 em produção e 200 na máquina de
            // quem o escreveu. A omissão usa a fonte que o QuestPDF traz
            // embutida, que existe em qualquer sítio onde o pacote exista.
            //
            // Para um documento fiscal isto vale mais do que a escolha
            // tipográfica: o papel sai igual em todos os ambientes.
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Element(e => Cabecalho(e, printout));
            page.Content().PaddingVertical(10).Element(e => Corpo(e, doc, comImposto));
            page.Footer().Element(e => Rodape(e, printout));
        }))

        // As datas dos metadados vêm do **documento**, não do relógio.
        //
        // O QuestPDF grava `/CreationDate` e `/ModDate` no PDF, e por omissão põe
        // lá o instante da composição. Isso fazia duas composições do mesmo
        // documento diferirem em dois campos — e foi assim que um teste de
        // determinismo passou localmente (as duas no mesmo segundo) e falhou na
        // CI (a atravessar a fronteira do segundo).
        //
        // Fixá-las na data do documento não é só higiene de teste: a data de
        // criação do ficheiro não tem significado nenhum num documento fiscal
        // reimpresso, e ter lá «hoje» num PDF de uma factura de Março é mais
        // enganador do que útil. Assim o papel de um documento é sempre o mesmo
        // papel, byte por byte, independentemente de quando foi composto.
        .WithMetadata(new DocumentMetadata
        {
            CreationDate = doc.IssuedOn.ToDateTime(TimeOnly.MinValue),
            ModifiedDate = doc.IssuedOn.ToDateTime(TimeOnly.MinValue),
        })
        .GeneratePdf();
    }

    private static void Cabecalho(IContainer container, FiscalDocumentPrintout printout)
    {
        var emitente = printout.Issuer;
        var doc = printout.Document;

        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(esquerda =>
                {
                    esquerda.Item().Text(emitente.CompanyName).Bold().FontSize(14);

                    if (!string.IsNullOrWhiteSpace(emitente.BusinessName))
                    {
                        esquerda.Item().Text(emitente.BusinessName!).FontSize(9).FontColor(Colors.Grey.Darken2);
                    }

                    esquerda.Item().PaddingTop(4).Text($"NIF: {emitente.TaxRegistrationNumber}");
                    esquerda.Item().Text(emitente.AddressDetail);
                    esquerda.Item().Text(Localidade(emitente.PostalCode, emitente.City, emitente.Country));

                    if (!string.IsNullOrWhiteSpace(emitente.Email))
                    {
                        esquerda.Item().Text(emitente.Email!);
                    }

                    if (!string.IsNullOrWhiteSpace(emitente.Phone))
                    {
                        esquerda.Item().Text(emitente.Phone!);
                    }
                });

                row.ConstantItem(200).Column(direita =>
                {
                    direita.Item().AlignRight().Text(doc.Title.ToUpperInvariant()).Bold().FontSize(16);
                    direita.Item().AlignRight().Text(doc.Number).FontSize(12);

                    direita.Item().PaddingTop(6).AlignRight()
                        .Text($"Data: {doc.IssuedOn.ToString("dd/MM/yyyy", Cultura)}");

                    if (doc.TaxPointDate is { } facto && facto != doc.IssuedOn)
                    {
                        direita.Item().AlignRight()
                            .Text($"Facto gerador: {facto.ToString("dd/MM/yyyy", Cultura)}");
                    }

                    if (!string.IsNullOrWhiteSpace(doc.Reference))
                    {
                        direita.Item().PaddingTop(4).AlignRight()
                            .Text($"Refere-se a: {doc.Reference}").Bold();
                    }

                    if (!string.IsNullOrWhiteSpace(doc.PaymentMethod))
                    {
                        direita.Item().AlignRight().Text($"Meio de pagamento: {doc.PaymentMethod}");
                    }
                });
            });

            // A anulação vai em cima e não em nota de pé: quem recebe o papel
            // tem de o saber antes de ler o valor, não depois.
            if (doc.Cancelled)
            {
                col.Item().PaddingTop(8).Background(Colors.Red.Lighten4)
                    .Border(1).BorderColor(Colors.Red.Darken1).Padding(6)
                    .Column(aviso =>
                    {
                        aviso.Item().Text("DOCUMENTO ANULADO").Bold().FontColor(Colors.Red.Darken3).FontSize(12);

                        if (!string.IsNullOrWhiteSpace(doc.CancellationReason))
                        {
                            aviso.Item().Text(doc.CancellationReason!).FontColor(Colors.Red.Darken3);
                        }
                    });
            }

            col.Item().PaddingTop(10).BorderTop(1).BorderColor(Colors.Grey.Lighten1);
        });
    }

    private static void Corpo(IContainer container, FiscalDocumentContent doc, bool comImposto)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(10).Column(cliente =>
            {
                cliente.Item().Text("Cliente").Bold().FontColor(Colors.Grey.Darken2);
                cliente.Item().Text(doc.Customer.Name).Bold();
                cliente.Item().Text($"NIF: {doc.Customer.TaxId}");

                if (!string.IsNullOrWhiteSpace(doc.Customer.AddressDetail))
                {
                    cliente.Item().Text(doc.Customer.AddressDetail!);
                    cliente.Item().Text(Localidade(null, doc.Customer.City, doc.Customer.Country));
                }

                if (doc.Customer.FinalConsumer)
                {
                    cliente.Item().Text("Consumidor final").Italic().FontColor(Colors.Grey.Darken1);
                }
            });

            col.Item().Table(tabela =>
            {
                tabela.ColumnsDefinition(colunas =>
                {
                    colunas.ConstantColumn(24);
                    colunas.RelativeColumn(4);
                    colunas.ConstantColumn(50);
                    colunas.ConstantColumn(70);

                    if (comImposto)
                    {
                        colunas.ConstantColumn(44);
                        colunas.ConstantColumn(70);
                    }

                    colunas.ConstantColumn(78);
                });

                tabela.Header(cabecalho =>
                {
                    Celula(cabecalho.Cell(), "#", true);
                    Celula(cabecalho.Cell(), "Descrição", true);
                    Celula(cabecalho.Cell(), "Qtd.", true, true);
                    Celula(cabecalho.Cell(), "Preço unit.", true, true);

                    if (comImposto)
                    {
                        Celula(cabecalho.Cell(), "Taxa", true, true);
                        Celula(cabecalho.Cell(), "Imposto", true, true);
                    }

                    Celula(cabecalho.Cell(), "Total", true, true);
                });

                foreach (var linha in doc.Lines)
                {
                    Celula(tabela.Cell(), linha.LineNumber.ToString(Cultura));
                    Celula(tabela.Cell(), linha.Description);
                    Celula(tabela.Cell(), Numero(linha.Quantity), alinharDireita: true);
                    Celula(tabela.Cell(), Numero(linha.UnitPrice), alinharDireita: true);

                    if (comImposto)
                    {
                        Celula(tabela.Cell(), $"{Numero(linha.TaxPercentage)}%", alinharDireita: true);
                        Celula(tabela.Cell(), Numero(linha.TaxAmount), alinharDireita: true);
                    }

                    Celula(tabela.Cell(), Numero(linha.NetAmount + linha.TaxAmount), alinharDireita: true);
                }
            });

            col.Item().PaddingTop(10).AlignRight().Width(240).Column(totais =>
            {
                if (comImposto)
                {
                    Total(totais, "Subtotal", doc.NetTotal, doc.Currency);
                    Total(totais, "Imposto", doc.TaxTotal, doc.Currency);
                }

                totais.Item().PaddingTop(3).BorderTop(1).BorderColor(Colors.Grey.Medium);
                Total(totais, "Total", doc.GrossTotal, doc.Currency, destacar: true);
            });

            if (!string.IsNullOrWhiteSpace(doc.Note))
            {
                col.Item().PaddingTop(12).Column(nota =>
                {
                    nota.Item().Text("Observações").Bold().FontColor(Colors.Grey.Darken2);
                    nota.Item().Text(doc.Note!);
                });
            }
        });
    }

    private static void Rodape(IContainer container, FiscalDocumentPrintout printout)
    {
        var doc = printout.Document;

        container.Column(col =>
        {
            col.Item().PaddingTop(6).BorderTop(1).BorderColor(Colors.Grey.Lighten1);

            // A menção fiscal vai como foi congelada na emissão. Não se
            // recalcula: o que vale é a menção exigida à data do documento.
            if (!string.IsNullOrWhiteSpace(doc.FiscalNotice))
            {
                col.Item().PaddingTop(4).Text(doc.FiscalNotice!).FontSize(8).Italic();
            }

            // Só sai se existir. Um número de validação inventado é pior do que
            // nenhum — ver TaxEntityProfile.SoftwareValidationNumber.
            if (!string.IsNullOrWhiteSpace(printout.Issuer.SoftwareValidationNumber))
            {
                col.Item().Text($"Processado por programa validado n.º {printout.Issuer.SoftwareValidationNumber}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            }

            col.Item().PaddingTop(2).AlignRight().Text(texto =>
            {
                texto.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                texto.Span("Página ");
                texto.CurrentPageNumber();
                texto.Span(" de ");
                texto.TotalPages();
            });
        });
    }

    /// <summary>
    /// Escreve uma célula já criada.
    ///
    /// <para>
    /// Recebe a célula e não a tabela porque em QuestPDF o cabeçalho e o corpo
    /// são descritores de tipos diferentes (<c>TableCellDescriptor</c> e
    /// <c>TableDescriptor</c>), ambos com <c>Cell()</c>. Passar o resultado de
    /// <c>Cell()</c> serve os dois sem duas sobrecargas.
    /// </para>
    /// </summary>
    private static void Celula(
        IContainer celula,
        string texto,
        bool cabecalho = false,
        bool alinharDireita = false)
    {
        var moldura = celula.Element(e => e
            .BorderBottom(cabecalho ? 1 : 0.5f)
            .BorderColor(cabecalho ? Colors.Grey.Medium : Colors.Grey.Lighten2)
            .PaddingVertical(4)
            .PaddingHorizontal(3));

        var conteudo = alinharDireita ? moldura.AlignRight() : moldura;
        var span = conteudo.Text(texto);

        if (cabecalho)
        {
            span.Bold();
        }
    }

    private static void Total(
        ColumnDescriptor coluna,
        string etiqueta,
        decimal valor,
        string moeda,
        bool destacar = false)
    {
        coluna.Item().PaddingVertical(2).Row(row =>
        {
            var esquerda = row.RelativeItem().Text(etiqueta);
            var direita = row.ConstantItem(120).AlignRight().Text($"{Numero(valor)} {moeda}");

            if (destacar)
            {
                esquerda.Bold().FontSize(11);
                direita.Bold().FontSize(11);
            }
        });
    }

    private static string Localidade(string? codigoPostal, string? cidade, string? pais) =>
        string.Join(
            " · ",
            new[] { codigoPostal, cidade, pais }.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>
    /// Duas casas decimais, sempre. Um documento fiscal não arredonda para
    /// menos: <c>1.000,5</c> onde se espera <c>1.000,50</c> parece um erro de
    /// impressão.
    /// </summary>
    private static string Numero(decimal valor) => valor.ToString("N2", Cultura);
}
