using Rivo.Documents.Domain;

namespace Rivo.Documents.Domain.Tests;

/// <summary>
/// Documento — `documents` guarda o ficheiro e os metadados, e não interpreta
/// o significado de negócio. A classificação e a retenção pertencem ao
/// contexto de origem (BR-15, ADR-009).
/// </summary>
public class DocumentTests
{
    private static readonly DateTimeOffset UploadedAt = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    private static Document Store(
        string fileName = "contrato.pdf",
        string contentType = "application/pdf",
        long sizeInBytes = 1024,
        string category = "contrato",
        string contentHash = "e3b0c44298fc1c14",
        string storagePath = "e3/b0/e3b0c44298fc1c14") =>
        Document.Store(
            fileName, contentType, sizeInBytes, category, contentHash, storagePath,
            uploadedBy: Guid.CreateVersion7(), uploadedAt: UploadedAt);

    // --- Validação --------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_WithoutFileName_Throws(string fileName)
    {
        Assert.Throws<ArgumentException>(() => Store(fileName: fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_WithoutCategory_Throws(string category)
    {
        Assert.Throws<ArgumentException>(() => Store(category: category));
    }

    /// <summary>
    /// O hash é o que permite verificar mais tarde que o ficheiro no
    /// armazenamento não foi alterado. Sem ele, a integridade não é
    /// verificável — e num domínio com retenção legal isso não é aceitável.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_WithoutContentHash_Throws(string contentHash)
    {
        Assert.Throws<ArgumentException>(() => Store(contentHash: contentHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_WithoutStoragePath_Throws(string storagePath)
    {
        Assert.Throws<ArgumentException>(() => Store(storagePath: storagePath));
    }

    /// <summary>
    /// Um ficheiro vazio quase sempre indica upload falhado. Aceitá-lo
    /// deixaria um anexo inútil ligado a um registo de negócio, e o erro só
    /// apareceria quando alguém tentasse abri-lo.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Store_WithEmptyContent_Throws(long sizeInBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Store(sizeInBytes: sizeInBytes));
    }

    // --- Normalização -----------------------------------------------------

    /// <summary>
    /// Tipo de conteúdo em falta não impede o armazenamento: recusar o
    /// ficheiro porque o cliente não anunciou o tipo seria perder o conteúdo
    /// por causa de metadados. Assume-se o tipo genérico.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_WithoutContentType_FallsBackToTheGenericType(string contentType)
    {
        var document = Store(contentType: contentType);

        Assert.Equal("application/octet-stream", document.ContentType);
    }

    [Fact]
    public void Store_TrimsFileNameAndCategory()
    {
        var document = Store(fileName: "  contrato.pdf  ", category: "  contrato  ");

        Assert.Equal("contrato.pdf", document.FileName);
        Assert.Equal("contrato", document.Category);
    }

    // --- Anulação lógica --------------------------------------------------

    [Fact]
    public void Store_StartsAvailable()
    {
        Assert.True(Store().IsAvailable);
    }

    /// <summary>
    /// BR-14: documentos sujeitos a retenção legal nunca são eliminados
    /// fisicamente. Anular é marcar, não apagar — o registo e o hash têm de
    /// sobreviver.
    /// </summary>
    [Fact]
    public void Void_MarksWithoutErasing()
    {
        var document = Store();
        var hash = document.ContentHash;

        document.Void(UploadedAt.AddYears(1));

        Assert.False(document.IsAvailable);
        Assert.Equal(hash, document.ContentHash);
        Assert.Equal("contrato.pdf", document.FileName);
    }

    /// <summary>Idempotente: o que interessa é quando foi anulado da primeira vez.</summary>
    [Fact]
    public void Void_KeepsTheInstantOfTheFirstVoid()
    {
        var document = Store();
        var first = UploadedAt.AddYears(1);

        document.Void(first);
        document.Void(UploadedAt.AddYears(2));

        Assert.Equal(first, document.VoidedAt);
    }
}
