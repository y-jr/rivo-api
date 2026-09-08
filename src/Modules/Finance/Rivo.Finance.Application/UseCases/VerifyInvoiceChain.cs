using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Domain;

namespace Rivo.Finance.Application.UseCases;

/// <summary>
/// Percorre a cadeia de integridade de cada série e reporta o que não fecha
/// (ADR-060).
///
/// <para>
/// <strong>Existe porque uma cadeia sem verificação não é uma
/// cadeia.</strong> <c>SalesInvoice.HashMatches</c> nasceu com o ADR-060 e não
/// tinha quem o chamasse fora dos testes — o que quer dizer que a adulteração
/// que ele detecta nunca chegava a ser detectada.
/// </para>
///
/// <para>
/// <strong>Duas quebras diferentes, e só a segunda exige percorrer.</strong>
/// Um documento alterado apanha-se sozinho, recalculando o hash dele. Um
/// documento <em>removido</em> ou <em>inserido</em> não: cada um dos que ficam
/// continua consistente consigo próprio, e só a ligação entre eles denuncia a
/// falha. É a razão de isto ser uma travessia e não uma verificação por
/// documento.
/// </para>
/// </summary>
public sealed class VerifyInvoiceChain(ISalesInvoiceStore store)
{
    public async Task<ChainVerification> ExecuteAsync(CancellationToken cancellationToken)
    {
        var series = await store.ListSeriesAsync(cancellationToken);

        var resultados = new List<SeriesChainVerification>(series.Count);

        foreach (var serie in series)
        {
            resultados.Add(
                await VerificarSerieAsync(serie, cancellationToken));
        }

        return new ChainVerification(resultados);
    }

    private async Task<SeriesChainVerification> VerificarSerieAsync(
        DocumentSeries serie,
        CancellationToken cancellationToken)
    {
        var facturas = await store.ListBySeriesAsync(serie.Type, serie.Code, cancellationToken);

        var quebras = new List<ChainBreak>();

        // O que se esperava encontrar no `PreviousHash` do próximo elo. Nulo
        // até à primeira factura com cadeia.
        string? esperado = null;
        var verificadas = 0;
        var anterioresACadeia = 0;

        foreach (var factura in facturas)
        {
            if (factura.Hash is null)
            {
                // Anterior à cadeia (ADR-060). Não é quebra e não pode contar
                // como uma: dizer que 147 facturas legítimas estão adulteradas
                // faria o relatório ser ignorado, que é como uma verificação
                // morre.
                anterioresACadeia++;
                continue;
            }

            if (!factura.HashMatches())
            {
                quebras.Add(ChainBreak.Of(
                    factura.Number.Formatted,
                    ChainBreakKind.DocumentAltered,
                    "O conteúdo do documento não corresponde ao elo gravado. "
                    + "Alguém alterou a factura fora da aplicação."));
            }

            // Comparado mesmo quando o elo próprio já falhou: são falhas
            // independentes, e saber as duas diz se o documento foi alterado,
            // se a sequência foi mexida, ou ambas.
            if (factura.PreviousHash != esperado)
            {
                quebras.Add(ChainBreak.Of(
                    factura.Number.Formatted,
                    ChainBreakKind.SequenceBroken,
                    esperado is null
                        ? "Aponta para um documento anterior, mas é o primeiro da cadeia nesta série."
                        : "Não aponta para o documento imediatamente anterior. "
                          + "Falta um documento na sequência, ou foi inserido um."));
            }

            esperado = factura.Hash;
            verificadas++;
        }

        // ⚠ O último elo da série tem de bater com o que a série guarda. Sem
        // esta comparação, remover as últimas facturas de uma série passaria
        // despercebido: as que sobram continuam encadeadas entre si.
        if (esperado != serie.LastDocumentHash)
        {
            quebras.Add(ChainBreak.Of(
                $"{serie.Type} {serie.Code}",
                ChainBreakKind.SeriesTailMismatch,
                "O último elo da série não é o da última factura encontrada. "
                + "Foram removidos documentos do fim da sequência."));
        }

        return new SeriesChainVerification(
            $"{serie.Type} {serie.Code}",
            verificadas,
            anterioresACadeia,
            quebras);
    }
}

/// <param name="Series">Uma entrada por série, mesmo as sem facturas.</param>
public sealed record ChainVerification(IReadOnlyList<SeriesChainVerification> Series)
{
    /// <summary>
    /// Verdadeiro quando nenhuma série tem quebras. É o que quem chama lê
    /// primeiro — a lista serve para saber onde.
    /// </summary>
    public bool Intact => Series.All(s => s.Breaks.Count == 0);
}

/// <param name="Verified">Facturas com elo, percorridas e comparadas.</param>
/// <param name="BeforeChain">
/// Facturas emitidas antes de a cadeia existir (ADR-060). Não são quebras, e
/// aparecem contadas para o número não desaparecer em silêncio.
/// </param>
public sealed record SeriesChainVerification(
    string Series,
    int Verified,
    int BeforeChain,
    IReadOnlyList<ChainBreak> Breaks);

/// <param name="Kind">
/// O tipo de quebra, <strong>como texto</strong>.
///
/// <para>
/// Não como o enumerado: o <c>System.Text.Json</c> serializa-o como inteiro, e
/// quem lesse a resposta via <c>"kind": 0</c> sem saber que zero é
/// «documento adulterado». É o mesmo defeito que
/// <c>SupplierReference.Status</c> teve, e foi apanhado da mesma maneira — a
/// olhar para o JSON que sai.
/// </para>
/// </param>
public sealed record ChainBreak(string Document, string Kind, string Detail)
{
    internal static ChainBreak Of(string document, ChainBreakKind kind, string detail) =>
        new(document, kind.ToString(), detail);
}

public enum ChainBreakKind
{
    /// <summary>O conteúdo do documento mudou depois de emitido.</summary>
    DocumentAltered,

    /// <summary>O elo anterior não é o que devia ser — falta ou sobra um documento.</summary>
    SequenceBroken,

    /// <summary>O fim da cadeia não coincide com o que a série guarda.</summary>
    SeriesTailMismatch,
}
