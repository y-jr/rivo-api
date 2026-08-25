namespace Rivo.Finance.Domain;

/// <summary>
/// Um diário. Agrupa lançamentos por natureza — vendas, compras, tesouraria,
/// operações diversas.
///
/// <para>
/// <c>Journal</c> no SAF-T, com <c>JournalID</c> único no ficheiro. Existe como
/// agregado próprio porque o <c>TransactionID</c> se constrói a partir dele, e
/// um diário que mudasse de identificador partiria as chaves de tudo o que já
/// foi lançado.
/// </para>
/// </summary>
public sealed class Journal
{
    private Journal(string code, string name)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private Journal()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary><c>JournalID</c> no SAF-T: até 30 caracteres, sem espaços.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Concorrência optimista (ADR-025, BR-17).</summary>
    public int Version { get; private set; }

    public static Journal Open(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Um diário precisa de código.", nameof(code));
        }

        var normalizado = code.Trim().ToUpperInvariant();

        // Sem espaços porque o `TransactionID` do SAF-T é a concatenação de
        // data, diário e número de arquivo **separada por espaços** — um espaço
        // no código tornaria a chave impossível de repartir.
        if (normalizado.Contains(' ') || normalizado.Length > 30)
        {
            throw new ArgumentException(
                "O código do diário vai para o `TransactionID`, que se separa por espaços: " +
                "até 30 caracteres e sem espaços.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Um diário precisa de descrição.", nameof(name));
        }

        return new Journal(normalizado, name.Trim());
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}

/// <summary>
/// Um lançamento contabilístico. <c>Transaction</c> no SAF-T.
///
/// <para>
/// <strong>A invariante é a partida dobrada:</strong> a soma dos débitos é
/// igual à soma dos créditos, e há pelo menos uma linha de cada. O XSD exige
/// as duas linhas; a igualdade é o que faz da contabilidade uma contabilidade,
/// e é imposta aqui — não numa verificação que alguém pode esquecer de chamar.
/// </para>
///
/// <para>
/// <strong>Lançado inteiro num acto só</strong>, como a factura de venda. Não
/// há rascunho nem forma de acrescentar linha depois: um lançamento a meio não
/// equilibra, e um estado "por equilibrar" seria dizer que a invariante é
/// opcional.
/// </para>
///
/// <para>
/// Corrigir faz-se com outro lançamento — de regularização (<c>R</c>) ou de
/// ajustamento (<c>J</c>), que o SAF-T distingue precisamente para isso.
/// </para>
/// </summary>
public sealed class JournalEntry
{
    private readonly List<JournalEntryLine> _lines = [];

    private JournalEntry(
        Guid journalId,
        string journalCode,
        string archivalNumber,
        DateOnly transactionDate,
        int period,
        string description,
        TransactionType type,
        string sourceId,
        DateTimeOffset postedAt)
    {
        Id = Guid.CreateVersion7();
        JournalId = journalId;
        JournalCode = journalCode;
        ArchivalNumber = archivalNumber;
        TransactionDate = transactionDate;
        Period = period;
        Description = description;
        Type = type;
        SourceId = sourceId;
        PostedAt = postedAt;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private JournalEntry()
    {
        JournalCode = string.Empty;
        ArchivalNumber = string.Empty;
        Description = string.Empty;
        SourceId = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid JournalId { get; private set; }

    /// <summary>
    /// O código do diário, congelado. Entra no <c>TransactionID</c>, e esse não
    /// pode mudar por o diário ter sido renomeado.
    /// </summary>
    public string JournalCode { get; private set; }

    /// <summary>
    /// <c>DocArchivalNumber</c>: como se encontra o documento físico no
    /// arquivo. Até 20 caracteres, sem espaços.
    /// </summary>
    public string ArchivalNumber { get; private set; }

    public DateOnly TransactionDate { get; private set; }

    /// <summary>
    /// Período contabilístico. <strong>1 a 16</strong>, tal como o XSD o
    /// restringe — os que passam de 12 são os de fecho e regularização.
    /// </summary>
    public int Period { get; private set; }

    public string Description { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>
    /// <c>SourceID</c>: quem lançou. Guardado como texto porque o SAF-T o
    /// define como identificador do utilizador, não como chave estrangeira.
    /// </summary>
    public string SourceId { get; private set; }

    /// <summary><c>GLPostingDate</c>: quando entrou nos livros.</summary>
    public DateTimeOffset PostedAt { get; private set; }

    public IReadOnlyList<JournalEntryLine> Lines => _lines;

    /// <summary>Soma dos débitos. Igual a <see cref="TotalCredit"/>, por construção.</summary>
    public decimal TotalDebit { get; private set; }

    public decimal TotalCredit { get; private set; }

    /// <summary>
    /// Concorrência optimista (ADR-025, BR-17). Um lançamento não se altera —
    /// mas anula-se, e é essa escrita que o contador protege.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// <c>TransactionID</c> do SAF-T: data, diário e número de arquivo,
    /// separados por espaços. É o XSD que fixa a composição.
    /// </summary>
    public string TransactionId => $"{TransactionDate:yyyy-MM-dd} {JournalCode} {ArchivalNumber}";

    public static JournalEntry Post(
        Journal journal,
        string archivalNumber,
        DateOnly transactionDate,
        int period,
        string description,
        TransactionType type,
        string sourceId,
        IReadOnlyList<NewJournalLine> lines,
        DateTimeOffset postedAt)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (!journal.IsActive)
        {
            throw new InvalidOperationException(
                $"O diário {journal.Code} está desactivado e não recebe lançamentos.");
        }

        if (string.IsNullOrWhiteSpace(archivalNumber))
        {
            throw new ArgumentException(
                "O número de arquivo é como se encontra o documento físico — não é opcional.",
                nameof(archivalNumber));
        }

        var arquivo = archivalNumber.Trim();

        if (arquivo.Contains(' ') || arquivo.Length > 20)
        {
            throw new ArgumentException(
                "O número de arquivo entra no `TransactionID`, que se separa por espaços: " +
                "até 20 caracteres e sem espaços.",
                nameof(archivalNumber));
        }

        // 1 a 16, e não 1 a 12: é o que o XSD restringe. Os períodos acima de
        // doze são os de fecho e regularização, e existem justamente para que o
        // apuramento de resultados não se misture com Dezembro.
        if (period is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(period), period, "O período contabilístico vai de 1 a 16 (SAF-T AO).");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Um lançamento precisa de descrição.", nameof(description));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("O lançamento regista quem o fez.", nameof(sourceId));
        }

        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("Um lançamento tem linhas.", nameof(lines));
        }

        var lancamento = new JournalEntry(
            journal.Id, journal.Code, arquivo, transactionDate, period,
            description.Trim(), type, sourceId.Trim(), postedAt);

        var numero = 1;

        foreach (var linha in lines)
        {
            lancamento._lines.Add(JournalEntryLine.From(lancamento.Id, numero++, linha, postedAt));
        }

        lancamento.TotalDebit = lancamento._lines
            .Where(l => l.Side is EntrySide.Debit)
            .Sum(l => l.Amount);

        lancamento.TotalCredit = lancamento._lines
            .Where(l => l.Side is EntrySide.Credit)
            .Sum(l => l.Amount);

        // O XSD exige pelo menos uma linha de cada lado. A razão não é de
        // formato: um lançamento só de débitos não diz de onde veio o dinheiro.
        if (lancamento.TotalDebit == 0m || lancamento.TotalCredit == 0m)
        {
            throw new UnbalancedEntryException(
                "Um lançamento tem pelo menos uma linha a débito e uma a crédito.");
        }

        if (lancamento.TotalDebit != lancamento.TotalCredit)
        {
            throw new UnbalancedEntryException(
                $"O lançamento não equilibra: {lancamento.TotalDebit:N2} a débito contra " +
                $"{lancamento.TotalCredit:N2} a crédito.");
        }

        return lancamento;
    }

    /// <summary>
    /// Anula o lançamento. Não elimina (BR-14) — as linhas ficam, e um
    /// lançamento anulado deixa de contar para saldos.
    ///
    /// <para>
    /// <strong>Não é o mesmo que estornar.</strong> Anular diz "isto nunca
    /// devia ter sido lançado"; estornar é lançar o inverso e é outro
    /// documento. Um período fechado só admite o segundo.
    /// </para>
    /// </summary>
    public void Void(string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Anular um lançamento exige motivo.", nameof(reason));
        }

        if (IsVoided)
        {
            throw new InvalidOperationException(
                "O lançamento já está anulado. O segundo motivo apagaria o primeiro.");
        }

        IsVoided = true;
        VoidedAt = at;
        VoidReason = reason.Trim();
    }

    public bool IsVoided { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }
}

/// <summary>
/// Uma linha de lançamento. <c>DebitLine</c> ou <c>CreditLine</c> no SAF-T,
/// distinguidas aqui por <see cref="Side"/> em vez de por duas colunas de
/// valor — duas colunas em que uma é sempre nula convidam a somar a errada.
/// </summary>
public sealed class JournalEntryLine
{
    private JournalEntryLine(
        Guid journalEntryId,
        int recordNumber,
        Guid accountId,
        string accountCode,
        EntrySide side,
        decimal amount,
        string description,
        Guid? costCentreId,
        string? sourceDocumentId,
        DateTimeOffset systemEntryDate)
    {
        Id = Guid.CreateVersion7();
        JournalEntryId = journalEntryId;
        RecordNumber = recordNumber;
        AccountId = accountId;
        AccountCode = accountCode;
        Side = side;
        Amount = amount;
        Description = description;
        CostCentreId = costCentreId;
        SourceDocumentId = sourceDocumentId;
        SystemEntryDate = systemEntryDate;
    }

    /// <summary>Construtor sem parâmetros para materialização pelo ORM.</summary>
    private JournalEntryLine()
    {
        AccountCode = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid JournalEntryId { get; private set; }

    /// <summary><c>RecordID</c>: a ordem da linha dentro do lançamento.</summary>
    public int RecordNumber { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>
    /// O código da conta, congelado. É o que o SAF-T exporta, e a conta pode
    /// ser renomeada — mas o código não muda, por isso a cópia não fica
    /// obsoleta.
    /// </summary>
    public string AccountCode { get; private set; }

    public EntrySide Side { get; private set; }

    /// <summary>Sempre positivo. O sentido está em <see cref="Side"/>.</summary>
    public decimal Amount { get; private set; }

    public string Description { get; private set; }

    /// <summary>
    /// Centro de custo, quando a linha é imputável a um.
    ///
    /// <para>
    /// Nulo não é omissão: nem toda a linha tem imputação analítica, e forçar
    /// uma faria aparecer um centro de custo "diversos" que não significa nada.
    /// </para>
    /// </summary>
    public Guid? CostCentreId { get; private set; }

    /// <summary><c>SourceDocumentID</c>: o documento que originou a linha.</summary>
    public string? SourceDocumentId { get; private set; }

    /// <summary><c>SystemEntryDate</c>: quando o sistema a registou.</summary>
    public DateTimeOffset SystemEntryDate { get; private set; }

    internal static JournalEntryLine From(
        Guid journalEntryId, int recordNumber, NewJournalLine line, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(line), line.Amount,
                "O valor de uma linha é maior que zero. O sentido está no lado, não no sinal.");
        }

        if (string.IsNullOrWhiteSpace(line.AccountCode))
        {
            throw new ArgumentException("A linha diz em que conta lança.", nameof(line));
        }

        if (string.IsNullOrWhiteSpace(line.Description))
        {
            throw new ArgumentException("A linha tem descrição.", nameof(line));
        }

        return new JournalEntryLine(
            journalEntryId,
            recordNumber,
            line.AccountId,
            line.AccountCode,
            line.Side,
            decimal.Round(line.Amount, 2, MidpointRounding.AwayFromZero),
            line.Description.Trim(),
            line.CostCentreId,
            string.IsNullOrWhiteSpace(line.SourceDocumentId) ? null : line.SourceDocumentId.Trim(),
            at);
    }
}

public sealed record NewJournalLine(
    Guid AccountId,
    string AccountCode,
    EntrySide Side,
    decimal Amount,
    string Description,
    Guid? CostCentreId = null,
    string? SourceDocumentId = null);

public enum EntrySide
{
    Debit,
    Credit,
}

/// <summary>
/// <c>TransactionType</c> do SAF-T AO, tal como o XSD o enumera.
/// </summary>
public enum TransactionType
{
    /// <summary>Normal.</summary>
    N,

    /// <summary>Regularizações do período de tributação.</summary>
    R,

    /// <summary>Apuramento de resultados.</summary>
    A,

    /// <summary>Movimentos de ajustamento.</summary>
    J,
}

/// <summary>
/// O lançamento não equilibra.
///
/// <para>
/// Excepção própria porque a fronteira HTTP tem de a distinguir de um campo mal
/// preenchido: é a invariante central da contabilidade, e quem a viola merece
/// ouvir exactamente isso em vez de "pedido inválido".
/// </para>
/// </summary>
public sealed class UnbalancedEntryException(string message) : InvalidOperationException(message);
