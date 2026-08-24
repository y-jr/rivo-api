using Rivo.Finance.Domain;

namespace Rivo.Finance.Domain.Tests;

/// <summary>
/// A série existe para impor a única invariante que uma factura não consegue
/// impor sozinha: a numeração é sequencial e sem duplicados.
/// </summary>
public class DocumentSeriesTests
{
    private static DocumentSeries Serie() => DocumentSeries.Open(DocumentType.FT, "S001");

    [Fact]
    public void NumeracaoComecaEmUm()
    {
        Assert.Equal(1, Serie().Allocate().Sequence);
    }

    [Fact]
    public void CadaAtribuicaoAvancaASequencia()
    {
        var serie = Serie();

        Assert.Equal(["FT S001/1", "FT S001/2", "FT S001/3"],
            new[] { serie.Allocate(), serie.Allocate(), serie.Allocate() }.Select(n => n.Formatted));
    }

    [Fact]
    public void CodigoEGuardadoEmMaiusculas()
    {
        Assert.Equal("S001", DocumentSeries.Open(DocumentType.FT, " s001 ").Code);
    }

    [Fact]
    public void SerieSemCodigo_ERecusada()
    {
        Assert.Throws<ArgumentException>(() => DocumentSeries.Open(DocumentType.FT, "  "));
    }

    [Fact]
    public void SerieFechada_NaoAtribuiMaisNumeros()
    {
        var serie = Serie();
        serie.Allocate();
        serie.Close();

        Assert.Throws<InvalidOperationException>(() => serie.Allocate());
    }

    /// <summary>
    /// Fechar não elimina: os documentos já emitidos continuam a referenciá-la
    /// e o histórico tem de continuar legível (BR-14).
    /// </summary>
    [Fact]
    public void FecharNaoRecuaOContador()
    {
        var serie = Serie();
        serie.Allocate();
        serie.Allocate();
        serie.Close();

        Assert.Equal(3, serie.NextSequence);
    }

    /// <summary>
    /// Se a emissão falhar depois de atribuído, o número fica queimado e a
    /// sequência ganha um buraco. É deliberado — reutilizar um número já
    /// atribuído poria dois documentos diferentes com o mesmo número.
    /// </summary>
    [Fact]
    public void NaoHaComoDevolverUmNumero()
    {
        Assert.Null(typeof(DocumentSeries).GetMethod("Release"));
        Assert.Null(typeof(DocumentSeries).GetMethod("Rollback"));
    }

    [Fact]
    public void ODominioNaoMexeNoContadorDeConcorrencia()
    {
        var serie = Serie();
        serie.Allocate();

        // O contador de concorrência é do DbContext; o `NextSequence` é que é
        // do domínio. Não confundir os dois.
        Assert.Equal(0, serie.Version);
        Assert.Equal(2, serie.NextSequence);
    }
}
