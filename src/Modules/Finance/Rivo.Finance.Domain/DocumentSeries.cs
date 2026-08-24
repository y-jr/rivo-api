namespace Rivo.Finance.Domain;

/// <summary>
/// A série de numeração de um tipo de documento, e o contador que a faz
/// avançar.
///
/// <para>
/// Existe como agregado próprio por causa de uma invariante que a factura não
/// consegue impor sozinha: <strong>a numeração é sequencial e sem
/// duplicados</strong> (`modules/fiscal.md`, do XSD do SAF-T). Isso é uma regra
/// sobre o conjunto de facturas da série, e uma factura não vê o conjunto.
/// </para>
///
/// <para>
/// Pô-la aqui tem uma consequência boa: duas emissões simultâneas competem pela
/// mesma linha de série, e o contador de concorrência faz uma perder com
/// <c>409</c> (ADR-025, ADR-035). Sem isto, sairiam duas facturas com o mesmo
/// número — que é o defeito que a cadeia de assinatura do SAF-T existe para
/// tornar impossível, e que não podemos detectar porque o ADR-036 a adiou.
/// </para>
/// </summary>
public sealed class DocumentSeries
{
    private DocumentSeries(Guid id, DocumentType type, string code)
    {
        Id = id;
        Type = type;
        Code = code;
        NextSequence = 1;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private DocumentSeries() => Code = string.Empty;

    public Guid Id { get; private set; }

    public DocumentType Type { get; private set; }

    /// <summary>Identificador da série, em maiúsculas. Ex.: <c>S001</c>.</summary>
    public string Code { get; private set; }

    /// <summary>
    /// O próximo número a atribuir. Começa em 1 — o SAF-T numera a partir de 1
    /// por série, não a partir de 0.
    /// </summary>
    public int NextSequence { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025).
    ///
    /// <para>
    /// <strong>Aqui não é formalidade.</strong> É o que impede duas emissões
    /// simultâneas de receberem o mesmo número. O domínio nunca lhe toca — quem
    /// o incrementa é o <c>SaveChangesAsync</c> do DbContext.
    /// </para>
    /// </summary>
    public int Version { get; private set; }

    public static DocumentSeries Open(DocumentType type, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Uma série precisa de código. Ex.: 'S001'.", nameof(code));
        }

        var normalizado = code.Trim().ToUpperInvariant();

        if (normalizado.Length > 20)
        {
            throw new ArgumentException("O código da série tem no máximo 20 caracteres.", nameof(code));
        }

        return new DocumentSeries(Guid.CreateVersion7(), type, normalizado);
    }

    /// <summary>
    /// Atribui o próximo número e avança o contador.
    ///
    /// <para>
    /// <strong>Não há como devolver um número.</strong> Se a emissão falhar
    /// depois disto, o número fica queimado e a sequência ganha um buraco. É
    /// deliberado: reutilizar um número atribuído seria pior — dois documentos
    /// diferentes com o mesmo número em momentos diferentes é precisamente o
    /// que a numeração existe para impedir.
    /// </para>
    /// </summary>
    public DocumentNumber Allocate()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException(
                $"A série {Code} está fechada e não atribui números novos.");
        }

        var numero = new DocumentNumber(Type, Code, NextSequence);
        NextSequence++;

        return numero;
    }

    /// <summary>
    /// Fecha a série. Não elimina — os documentos já emitidos continuam a
    /// referenciá-la, e o histórico tem de continuar legível (BR-14).
    /// </summary>
    public void Close() => IsActive = false;
}

/// <summary>
/// Tipos de documento do SAF-T AO.
///
/// <para>
/// <strong>Só a factura, por agora.</strong> O XSD define FT, FR, GF, FG, NC,
/// ND, AC, AR, AF e TV (`modules/fiscal.md`), mas cada um traz regras próprias
/// — a nota de crédito tem de referenciar o documento que corrige, a guia tem
/// de transportar datas e locais de movimento. Declarar aqui um tipo que
/// ninguém sabe emitir correctamente convidaria a emiti-lo mal.
/// </para>
/// </summary>
public enum DocumentType
{
    /// <summary>Factura.</summary>
    FT,
}

/// <summary>
/// Número de documento fiscal: <c>[Tipo] [Série]/[Sequencial]</c>.
///
/// <para>
/// Objecto de valor, e imutável. A forma é a que o SAF-T exige — ex.:
/// <c>FT S001/1</c>.
/// </para>
/// </summary>
public sealed class DocumentNumber
{
    internal DocumentNumber(DocumentType type, string series, int sequence)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence), sequence, "A numeração de uma série começa em 1.");
        }

        Type = type;
        Series = series;
        Sequence = sequence;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private DocumentNumber() => Series = string.Empty;

    public DocumentType Type { get; private set; }

    public string Series { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>A forma normalizada, tal como aparece no documento.</summary>
    public string Formatted => $"{Type} {Series}/{Sequence}";

    public override string ToString() => Formatted;
}
