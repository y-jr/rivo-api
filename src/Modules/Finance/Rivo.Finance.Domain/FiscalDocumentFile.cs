namespace Rivo.Finance.Domain;

/// <summary>
/// O ficheiro de um documento fiscal já emitido — a ligação entre a factura
/// (dados) e o PDF (papel).
///
/// <para>
/// <strong>Gera-se uma vez e congela.</strong> Um documento fiscal não muda
/// depois de emitido, e o seu papel também não pode mudar: duas impressões da
/// mesma factura têm de sair iguais ao byte. Por isso o ficheiro é gerado no
/// primeiro pedido, guardado em <c>documents</c>, e daí em diante é o mesmo
/// ficheiro que se devolve — não uma nova composição a cada descarga.
/// </para>
///
/// <para>
/// <strong>Append-only, como tudo o que é histórico aqui (BR-14).</strong> Nunca
/// se apaga nem substitui uma linha: acrescenta-se. É o que permite provar,
/// mais tarde, que papel existiu e quando.
/// </para>
///
/// <para>
/// <strong>O cancelamento é o caso que obriga a mais do que uma linha.</strong>
/// Uma factura anulada não pode ser entregue a um cliente com o aspecto de uma
/// factura boa — a anulação tem de aparecer impressa. Mas a versão emitida antes
/// da anulação também não se pode reescrever, porque foi ela que circulou. Logo
/// há, no máximo, duas linhas por documento: a de antes da anulação e a de
/// depois. <see cref="ReflectsCancellation"/> distingue-as, e é o que se procura
/// ao servir o pedido.
/// </para>
/// </summary>
public sealed class FiscalDocumentFile
{
    private FiscalDocumentFile() => ContentHash = string.Empty;

    private FiscalDocumentFile(
        FiscalDocumentKind kind,
        Guid sourceDocumentId,
        Guid storedDocumentId,
        string contentHash,
        bool reflectsCancellation,
        DateTimeOffset generatedAt)
    {
        Id = Guid.CreateVersion7();
        Kind = kind;
        SourceDocumentId = sourceDocumentId;
        StoredDocumentId = storedDocumentId;
        ContentHash = contentHash;
        ReflectsCancellation = reflectsCancellation;
        GeneratedAt = generatedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Contador de concorrência optimista (ADR-002, ADR-025). Presente por
    /// uniformidade — na prática estas linhas não se alteram depois de escritas.
    /// </summary>
    public int Version { get; private set; }

    public FiscalDocumentKind Kind { get; private set; }

    /// <summary>
    /// A factura, nota de crédito ou recibo a que este papel pertence.
    ///
    /// <para>
    /// Sem chave estrangeira, e de propósito: são três tabelas de origem
    /// possíveis e <see cref="Kind"/> é que diz qual. Uma FK polimórfica não se
    /// declara em EF sem inventar uma hierarquia que o domínio não tem.
    /// </para>
    /// </summary>
    public Guid SourceDocumentId { get; private set; }

    /// <summary>
    /// O ficheiro em <c>documents</c>. Identificador, sem chave estrangeira
    /// entre schemas de módulos (ADR-010).
    /// </summary>
    public Guid StoredDocumentId { get; private set; }

    /// <summary>
    /// SHA-256 do conteúdo, como <c>documents</c> o calculou ao guardar.
    ///
    /// <para>
    /// Duplicado aqui por uma razão prática: permite verificar que o ficheiro
    /// servido é o que foi gerado sem ter de o ir buscar e voltar a resumir. Num
    /// documento fiscal, poder responder «é este e não outro» vale a coluna.
    /// </para>
    /// </summary>
    public string ContentHash { get; private set; }

    /// <summary>
    /// Se o papel gerado mostra a anulação. Ver a nota sobre o cancelamento no
    /// resumo da classe.
    /// </summary>
    public bool ReflectsCancellation { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public static FiscalDocumentFile Record(
        FiscalDocumentKind kind,
        Guid sourceDocumentId,
        Guid storedDocumentId,
        string contentHash,
        bool reflectsCancellation,
        DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        return new FiscalDocumentFile(
            kind, sourceDocumentId, storedDocumentId,
            contentHash, reflectsCancellation, generatedAt);
    }
}

/// <summary>
/// Que documento fiscal é. Os três que `finance`/AR emite e entrega.
/// </summary>
public enum FiscalDocumentKind
{
    SalesInvoice,
    CreditNote,
    Receipt,
}
