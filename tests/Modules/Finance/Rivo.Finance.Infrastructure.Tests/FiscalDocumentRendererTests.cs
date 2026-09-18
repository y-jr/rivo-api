using System.Text;
using Rivo.Finance.Application.Abstractions;
using Rivo.Finance.Infrastructure.Documents;
using Rivo.Fiscal.Contracts;

namespace Rivo.Finance.Infrastructure.Tests;

/// <summary>
/// A composição do papel (ADR-066).
///
/// <para>
/// Não se verifica aqui o aspecto — isso vê-se abrindo o ficheiro. Verifica-se o
/// que um teste pode afirmar sem interpretar PDF: que sai um PDF válido, que sai
/// <strong>o mesmo</strong> PDF para o mesmo documento, e que os três tipos de
/// documento passam pelo compositor sem rebentar.
/// </para>
/// </summary>
public sealed class FiscalDocumentRendererTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 18);

    private static TaxEntityProfileView Emitente(string? validacao = null) => new(
        "Rivo Testes, Lda.",
        "Rivo",
        "5000000000",
        "Rua Principal 1",
        "Luanda",
        "1000",
        "AO",
        "geral@rivo.ao",
        "+244 900 000 000",
        validacao);

    private static FiscalDocumentContent Factura(bool anulada = false) => new(
        "Factura",
        "FT 2026/1",
        Hoje,
        Hoje,
        "AOA",
        new FiscalDocumentParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO", false),
        [
            new FiscalDocumentLine(1, "Serviço de consultoria", 2m, 50_000m, "NOR", 14m, 100_000m, 14_000m),
            new FiscalDocumentLine(2, "Deslocação", 1m, 10_000m, "NOR", 14m, 10_000m, 1_400m),
        ],
        110_000m,
        15_400m,
        125_400m,
        "IVA - regime geral",
        anulada,
        anulada ? "Erro de facturação" : null);

    private static FiscalDocumentContent Recibo() => new(
        "Recibo",
        "RG 2026/3",
        Hoje,
        null,
        "AOA",
        new FiscalDocumentParty("Refriango", "5417654321", "Rua Rainha Ginga 12", "Luanda", "AO", false),
        [new FiscalDocumentLine(1, "Liquidação da factura FT 2026/1", 1m, 50_000m, "", 0m, 50_000m, 0m)],
        50_000m,
        0m,
        50_000m,
        null,
        false,
        PaymentMethod: "TB");

    private static FiscalDocumentContent NotaDeCredito() => new(
        "Nota de crédito",
        "NC 2026/1",
        Hoje,
        Hoje,
        "AOA",
        new FiscalDocumentParty("Consumidor final", "CONSUMIDORFINAL", null, null, null, true),
        [new FiscalDocumentLine(1, "Devolução", 1m, 10_000m, "NOR", 14m, 10_000m, 1_400m)],
        10_000m,
        1_400m,
        11_400m,
        "IVA - regime geral",
        false,
        Reference: "FT 2026/1",
        Note: "Mercadoria devolvida");

    private static bool EhPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF";

    [Fact]
    public void Factura_ProduzUmPdf()
    {
        var bytes = new FiscalDocumentRenderer().Render(
            new FiscalDocumentPrintout(Emitente(), Factura()));

        Assert.True(EhPdf(bytes), "O que saiu não começa por %PDF.");

        // Um PDF de uma página com este conteúdo não cabe em menos de 1 KB. O
        // limiar existe para apanhar o caso de sair um documento vazio.
        Assert.True(bytes.Length > 1024, $"O PDF tem só {bytes.Length} bytes.");
    }

    [Fact]
    public void Recibo_ProduzUmPdf()
    {
        var bytes = new FiscalDocumentRenderer().Render(
            new FiscalDocumentPrintout(Emitente(), Recibo()));

        Assert.True(EhPdf(bytes));
    }

    [Fact]
    public void NotaDeCredito_ProduzUmPdf()
    {
        var bytes = new FiscalDocumentRenderer().Render(
            new FiscalDocumentPrintout(Emitente(), NotaDeCredito()));

        Assert.True(EhPdf(bytes));
    }

    [Fact]
    public void FacturaAnulada_ProduzUmPdfEDiferenteDaBoa()
    {
        var compositor = new FiscalDocumentRenderer();

        var boa = compositor.Render(new FiscalDocumentPrintout(Emitente(), Factura()));
        var anulada = compositor.Render(new FiscalDocumentPrintout(Emitente(), Factura(anulada: true)));

        Assert.True(EhPdf(anulada));

        // O aviso de anulação tem de mudar o documento. Se saíssem iguais, o
        // papel da anulada era indistinguível do da boa — que é precisamente o
        // que não pode acontecer.
        Assert.NotEqual(boa, anulada);
    }

    /// <summary>
    /// <strong>A propriedade que sustenta "compor uma vez".</strong>
    ///
    /// <para>
    /// Se a composição não fosse determinística, recompor um documento — o que
    /// acontece quando o ficheiro arquivado desaparece (K12) — dava um papel
    /// diferente do que circulou.
    /// </para>
    ///
    /// <para>
    /// <strong>Há um segundo de espera, e é o ponto do teste.</strong> Este teste
    /// já existiu sem ela: passava localmente, porque as duas composições caíam no
    /// mesmo segundo, e falhava na CI quando atravessavam a fronteira. A causa era
    /// o <c>/CreationDate</c> que o QuestPDF grava por omissão com o instante da
    /// composição. Sem a espera, o teste voltaria a mentir das duas maneiras:
    /// passaria com o defeito presente e seria intermitente.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MesmoDocumento_ProduzOsMesmosBytes_MesmoComTempoPeloMeio()
    {
        var compositor = new FiscalDocumentRenderer();
        var printout = new FiscalDocumentPrintout(Emitente(), Factura());

        var primeira = compositor.Render(printout);

        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        var segunda = compositor.Render(printout);

        Assert.Equal(primeira, segunda);
    }

    /// <summary>
    /// Duas instâncias diferentes do compositor também dão o mesmo papel — o
    /// determinismo não depende de estado guardado na instância.
    /// </summary>
    [Fact]
    public void CompositoresDiferentes_ProduzemOsMesmosBytes()
    {
        var printout = new FiscalDocumentPrintout(Emitente(), Factura());

        Assert.Equal(
            new FiscalDocumentRenderer().Render(printout),
            new FiscalDocumentRenderer().Render(printout));
    }

    /// <summary>
    /// O número de validação da AGT só sai se existir — ver
    /// <c>TaxEntityProfile.SoftwareValidationNumber</c>.
    /// </summary>
    [Fact]
    public void NumeroDeValidacao_MudaODocumentoQuandoExiste()
    {
        var compositor = new FiscalDocumentRenderer();

        var sem = compositor.Render(new FiscalDocumentPrintout(Emitente(), Factura()));
        var com = compositor.Render(new FiscalDocumentPrintout(Emitente("12345/AGT/2026"), Factura()));

        Assert.NotEqual(sem, com);
    }

    /// <summary>
    /// Um consumidor final não tem endereço no documento. Compor tem de
    /// funcionar sem ele, em vez de rebentar num campo nulo.
    /// </summary>
    [Fact]
    public void ConsumidorFinalSemEndereco_NaoRebenta()
    {
        var bytes = new FiscalDocumentRenderer().Render(
            new FiscalDocumentPrintout(Emitente(), NotaDeCredito()));

        Assert.True(EhPdf(bytes));
    }
}
